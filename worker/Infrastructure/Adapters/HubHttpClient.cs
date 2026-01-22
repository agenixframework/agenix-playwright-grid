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

using System.Net.Http.Json;
using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using WorkerService.Application.Ports;

namespace WorkerService.Infrastructure.Adapters;

public sealed class HubHttpClient : IHubClient
{
    private readonly ChunkedLogger<HubHttpClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public HubHttpClient(ChunkedLogger<HubHttpClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<bool> RegisterAsync(
        string hubUrl,
        string nodeSecret,
        string nodeId,
        string baseUrl,
        IEnumerable<string> apps,
        int capacity,
        IReadOnlyDictionary<string, string> labels,
        string? playwrightVersion = null,
        CancellationToken ct = default)
    {
        using var operation = _logger.BeginOperation("RegisterWorker", new Dictionary<string, object>
        {
            ["NodeId"] = nodeId,
            ["HubUrl"] = hubUrl,
            ["BaseUrl"] = baseUrl,
            ["Capacity"] = capacity
        });

        _logger.LogMilestone(EventCodes.Worker.RegistrationStarted,
            "Worker registration started for node {NodeId} at {HubUrl}", nodeId, hubUrl);

        var url = $"{hubUrl.TrimEnd('/')}/node/register";

        // Extract Region/OS from labels for browser metadata
        var region = labels.TryGetValue("region", out var r) ? r : "local";
        var os = labels.TryGetValue("os", out var o) ? o : Environment.OSVersion.Platform.ToString();
        var regionOs = $"{region}/{os}";

        var body = new
        {
            NodeId = nodeId,
            BaseUrl = baseUrl,
            Apps = apps.ToArray(),
            Capacity = capacity,
            Labels = labels,
            RegionOs = regionOs,
            PlaywrightVersion = playwrightVersion
        };

        // Log config and body (chunked)
        _logger.LogInformation(null, "Registration payload: {Payload}", JsonSerializer.Serialize(body));
        _logger.LogInformation(null, "Client configuration: Timeout=10s, AllowAutoRedirect=false, x-hub-secret=[REDACTED]");

        using var client = _httpClientFactory.CreateClient("HubClient");
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Remove("x-hub-secret");
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-hub-secret", nodeSecret);

        const int maxAttempts = 6;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogMilestone(EventCodes.Worker.RegistrationSent,
                    "Sending registration request to {Url} (attempt {Attempt}/{MaxAttempts})", url, attempt, maxAttempts);

                var resp = await client.PostAsJsonAsync(url, body, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);

                var responseLogProps = new Dictionary<string, object>
                {
                    ["StatusCode"] = (int)resp.StatusCode,
                    ["IsSuccess"] = resp.IsSuccessStatusCode,
                    ["Attempt"] = attempt
                };

                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogMilestone(EventCodes.Worker.RegistrationConfirmed,
                        "Hub confirmed successful worker registration (Status: {StatusCode})", resp.StatusCode);

                    operation.SetOutputs(new Dictionary<string, object> { ["Response"] = text });
                    operation.Complete();
                    return true;
                }

                _logger.LogWarning(EventCodes.Worker.RegistrationFailed,
                    "Registration failed with status {StatusCode}: {Response}", (int)resp.StatusCode, text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, EventCodes.Worker.RegistrationFailed,
                    "Error during registration attempt {Attempt}: {Message}", attempt, ex.Message);

                if (attempt == maxAttempts)
                {
                    operation.Fail(ex, ErrorType.DependencyFailure, DependencyName.Hub);
                }
            }

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                _logger.LogInformation(null, "Waiting {Delay} before next registration attempt...", delay);
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(null, "Registration cancelled during backoff delay.");
                    break;
                }
            }
        }

        _logger.LogError(null, EventCodes.Worker.RegistrationFailed, "Giving up worker registration after {MaxAttempts} attempts.", maxAttempts);
        return false;
    }

    public async Task<HubDiagnosticsDto?> GetDiagnosticsAsync(string hubUrl, CancellationToken ct = default)
    {
        using var operation = _logger.BeginOperation("GetHubDiagnostics", new Dictionary<string, object>
        {
            ["HubUrl"] = hubUrl
        });

        _logger.LogMilestone(EventCodes.Worker.RegistrationVerificationStarted,
            "Retrieving hub diagnostics from {HubUrl}", hubUrl);

        var url = $"{hubUrl.TrimEnd('/')}/diagnostics";

        try
        {
            using var client = _httpClientFactory.CreateClient("HubClient");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(url, ct);
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(EventCodes.Worker.RegistrationVerificationFailed,
                    "Failed to retrieve diagnostics. Status: {StatusCode}", statusCode);

                operation.Fail(new HttpRequestException($"Status code: {statusCode}"), ErrorType.DependencyFailure, DependencyName.Hub);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation(null, "Received diagnostics payload: {Payload}", json);

            var result = JsonSerializer.Deserialize<HubDiagnosticsDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogMilestone(EventCodes.Worker.RegistrationVerificationSucceeded,
                "Successfully retrieved diagnostics from {HubUrl}", hubUrl);

            operation.Complete();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, EventCodes.Worker.RegistrationVerificationFailed,
                "Error retrieving diagnostics from {Url}: {Message}", url, ex.Message);

            operation.Fail(ex, ErrorType.DependencyFailure, DependencyName.Hub);
            return null;
        }
    }
}
