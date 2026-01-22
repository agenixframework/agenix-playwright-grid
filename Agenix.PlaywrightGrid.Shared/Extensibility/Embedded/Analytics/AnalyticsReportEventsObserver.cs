#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Reflection;
using System.Text;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using Agenix.PlaywrightGrid.Shared.Reporter;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Embedded.Analytics;

/// <summary>
///     Google Analytics launch events tracker.
/// </summary>
public class AnalyticsReportEventsObserver : IReportEventsObserver, IDisposable
{
    private const string CLIENT_INFO = "Ry1XUDU3UlNHOFhMOkVGaGFqc2J3U3RTbmEtc0NydGN6RHc=";
    private const string BASE_URI = "https://www.google-analytics.com";
    private const string CLIENT_NAME = "commons-dotnet";
    private const string EVENT_NAME = "start_launch";

    private static string _agentVersion;
    private readonly string _apiKey;

    private readonly string _clientVersion;
    private readonly object _httpClientLock = new();

    private readonly string _measurementId;

    private readonly string _platformVersion;

    private HttpClient _httpClient;

    private IReportEventsSource _reportEventsSource;

    private Task _sendGaUsageTask;

    /// <summary>
    ///     Create an instance of AnalyticsReportEventsObserver object, construct own HttpClient if neccessary.
    /// </summary>
    public AnalyticsReportEventsObserver()
    {
        // Client is this assembly
        _clientVersion = typeof(AnalyticsReportEventsObserver).Assembly.GetName().Version.ToString(3);

#if NETSTANDARD
            _platformVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#else
        _platformVersion = AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName;
#endif
        var clientInfo = Encoding.UTF8.GetString(Convert.FromBase64String(CLIENT_INFO)).Split(':');
        _measurementId = clientInfo[0];
        _apiKey = clientInfo[1];
    }

    /// <summary>
    ///     Create an instance of AnalyticsReportEventsObserver object, use provided HttpMessageHandler to construct an
    ///     HttpClient.
    /// </summary>
    /// <param name="httpHandler">Http handler to construc a client</param>
    public AnalyticsReportEventsObserver(HttpMessageHandler httpHandler) : this()
    {
        _httpClient = new HttpClient(httpHandler) { BaseAddress = new Uri(BASE_URI) };
    }

    private static ITraceLogger TraceLogger => TraceLogManager.Instance.GetLogger<AnalyticsReportEventsObserver>();

    /// <summary>
    ///     Set the name of the Agent or use default name "Anonymous".
    /// </summary>
    /// <returns>The Agent name.</returns>
    public static string AgentName { get; private set; } = "Anonymous";

    /// <summary>
    ///     Return the version of the Agent retrieved from Assembly.
    /// </summary>
    /// <returns>The Agent version.</returns>
    public static string AgentVersion
    {
        get
        {
            if (string.IsNullOrEmpty(_agentVersion))
            {
                var agentAssemblyName = Assembly.GetCallingAssembly().GetName();
                _agentVersion = agentAssemblyName.Version.ToString(3);
            }

            return _agentVersion;
        }
    }

    /// <summary>
    ///     Release HttpClient if needed.
    /// </summary>
    public void Dispose()
    {
        if (_reportEventsSource != null)
        {
            _reportEventsSource.OnBeforeLaunchStarting -= ReportEventsSource_OnBeforeLaunchStarting;
            _reportEventsSource.OnAfterLaunchFinished -= ReportEventsSource_OnAfterLaunchFinished;
        }

        _httpClient?.Dispose();
    }

    /// <inheritdoc />
    public void Initialize(IReportEventsSource reportEventsSource)
    {
        _reportEventsSource = reportEventsSource ?? throw new ArgumentNullException(nameof(reportEventsSource));
        reportEventsSource.OnBeforeLaunchStarting += ReportEventsSource_OnBeforeLaunchStarting;
        reportEventsSource.OnAfterLaunchFinished += ReportEventsSource_OnAfterLaunchFinished;
    }

    /// <summary>
    ///     Sets custom information about agent name and version. It's expected this method is invoked on agent side.
    /// </summary>
    /// <param name="agentName">Human readable name of the agent.</param>
    /// <param name="agentVersion">Automatically identified as calling assembly version if null.</param>
    public static void DefineConsumer(string agentName, string agentVersion = null)
    {
        // determine agent name
        if (string.IsNullOrEmpty(agentName))
        {
            var agentAssemblyName = Assembly.GetCallingAssembly().GetName();
            AgentName = agentAssemblyName.Name;
        }
        else
        {
            AgentName = agentName;
        }

        // determine agent version
        if (string.IsNullOrEmpty(agentVersion))
        {
            var agentAssemblyName = Assembly.GetCallingAssembly().GetName();
            _agentVersion = agentAssemblyName.Version.ToString(3);
        }
        else
        {
            _agentVersion = agentVersion;
        }
    }

    private HttpClient GetHttpClient(IConfiguration configuration)
    {
        if (_httpClient != null)
        {
            return _httpClient;
        }

        lock (_httpClientLock)
        {
            if (_httpClient != null)
            {
                return _httpClient;
            }

            var handler = new HttpClientHandler();
            var ignoreSslErrors = configuration.GetValue("Server:IgnoreSslErrors", false);

#if NET462
                if (ignoreSslErrors)
                {
                    ServicePointManager.ServerCertificateValidationCallback +=
 (sender, cert, chain, sslPolicyErrors) => true;
                }
#else
            if (ignoreSslErrors)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }
#endif
            _httpClient = new HttpClient(handler) { BaseAddress = new Uri(BASE_URI) };
        }

        return _httpClient;
    }

    private void ReportEventsSource_OnBeforeLaunchStarting(ILaunchReporter launchReporter,
        BeforeLaunchStartingEventArgs args)
    {
        if (args.Configuration.GetValue("Analytics:Enabled", true))
        {
            // schedule tracking request
            _sendGaUsageTask = Task.Run(async () =>
            {
                var requestParams = new Dictionary<string, string>
                {
                    { "client_name", CLIENT_NAME },
                    { "client_version", _clientVersion },
                    { "interpreter", _platformVersion },
                    { "agent_name", AgentName },
                    { "agent_version", AgentVersion }
                };

                var eventData = new Dictionary<string, object> { { "name", EVENT_NAME }, { "params", requestParams } };

                var requestUri = $"/mp/collect?measurement_id={_measurementId}&api_secret={_apiKey}";

                var httpClient = GetHttpClient(args.Configuration);

                var payload = new Dictionary<string, object>
                {
                    { "client_id", await ClientIdProvider.GetClientIdAsync() },
                    { "events", new List<object> { eventData } }
                };

                string content;

                using (var stream = new MemoryStream())
                {
                    await JsonSerializer.SerializeAsync(stream, payload, payload.GetType());
                    stream.Position = 0;
                    using var reader = new StreamReader(stream);
                    content = await reader.ReadToEndAsync();
                }

                var stringContent = new StringContent(content, Encoding.UTF8, "application/json");

                try
                {
                    using var response = await httpClient.PostAsync(requestUri, stringContent);
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception exp)
                {
                    TraceLogger.Error($"Cannot track OnBeforeLaunchStarting event: {exp}");
                }
            });
        }
    }

    private void ReportEventsSource_OnAfterLaunchFinished(ILaunchReporter launchReporter,
        AfterLaunchFinishedEventArgs args)
    {
        _sendGaUsageTask?.GetAwaiter().GetResult();
    }
}
