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

using System.Collections.Concurrent;
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Extensibility;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Internal.Delegating;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using ReportPortal.Client.Abstractions;

namespace Agenix.PlaywrightGrid.Shared.Reporter;

public class LogsReporter : ILogsReporter
{
    private readonly bool _asyncReporting;
    private readonly IConfiguration _configuration;
    private readonly IExtensionManager _extensionManager;
    private readonly ILogRequestAmender _logRequestAmender;

    private readonly BlockingCollection<CreateLogItemRequest> _queue = [];
    private readonly IReporter _reporter;

    private readonly ReportEventsSource _reportEventsSource;
    private readonly IRequestExecutor _requestExecuter;
    private readonly IClientService _service;

    public LogsReporter(IReporter testReporter,
        IClientService service,
        IConfiguration configuration,
        IExtensionManager extensionManager,
        IRequestExecutor requestExecuter,
        ILogRequestAmender logRequestAmender,
        ReportEventsSource reportEventsSource,
        int batchCapacity)
    {
        _reporter = testReporter;
        _service = service;
        _configuration = configuration;
        _extensionManager = extensionManager;
        _requestExecuter = requestExecuter;
        _logRequestAmender = logRequestAmender;
        _reportEventsSource = reportEventsSource;
        _asyncReporting = _configuration.GetValue(ConfigurationPath.AsyncReporting, false);

        if (batchCapacity < 1)
        {
            throw new ArgumentException("Batch capacity for logs processing cannot be less than 1.",
                nameof(batchCapacity));
        }

        BatchCapacity = batchCapacity;

        ProcessingTask = _reporter.StartTask.ContinueWith(async consumer =>
        {
            await ConsumeLogRequests();
        }).Unwrap();
    }

    private static ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<LogsReporter>();

    public int BatchCapacity { get; }

    public Task ProcessingTask { get; }

    public void Log(CreateLogItemRequest logRequest)
    {
        _queue.Add(logRequest);
    }

    public void Sync()
    {
        try
        {
            Finish();

            ProcessingTask?.GetAwaiter().GetResult();
        }
        catch
        {
            // we don't aware of failed requests for sending log messages (for now)
        }
    }

    public void Finish()
    {
        _queue.CompleteAdding();
    }

    private async Task ConsumeLogRequests()
    {
        try
        {
            foreach (var logRequest in _queue.GetConsumingEnumerable())
            {
                if (logRequest.Attach != null)
                {
                    await SendLogRequests([logRequest]);
                }
                else
                {
                    var buffer = new List<CreateLogItemRequest> { logRequest };

                    for (var i = 0; i < BatchCapacity - 1; i++)
                    {
                        if (_queue.TryTake(out var nextLogRequest))
                        {
                            if (nextLogRequest.Attach != null)
                            {
                                await SendLogRequests(buffer);

                                buffer.Clear();

                                await SendLogRequests([nextLogRequest]);
                            }
                            else
                            {
                                buffer.Add(nextLogRequest);
                            }
                        }
                    }

                    if (buffer.Count > 0)
                    {
                        await SendLogRequests(buffer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TraceLogger.Error($"Unexpected error occurred while processing buffered log requests. {ex}");
        }
    }

    private async Task SendLogRequests(List<CreateLogItemRequest> logRequests)
    {
        // only if parent reporter is successful
        if (!_reporter.StartTask.IsFaulted && !_reporter.StartTask.IsCanceled)
        {
            try
            {
                foreach (var logItemRequest in logRequests)
                {
                    _logRequestAmender.Amend(logItemRequest);
                }

                NotifySending(logRequests);

                // Create log items individually (API doesn't support batch creation)
                foreach (var logRequest in logRequests)
                {
                    if (_asyncReporting)
                    {
                        await _requestExecuter
                            .ExecuteAsync(() => _service.AsyncLogItem.CreateAsync(logRequest), null,
                                _reporter.StatisticsCounter.LogItemStatisticsCounter)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await _requestExecuter
                            .ExecuteAsync(() => _service.LogItem.CreateAsync(logRequest), null,
                                _reporter.StatisticsCounter.LogItemStatisticsCounter)
                            .ConfigureAwait(false);
                    }
                }

                NotifySent(logRequests.AsReadOnly());
            }
            catch (Exception ex)
            {
                TraceLogger.Error($"Unexpected error occurred while sending log requests. {ex}");
            }
        }
    }

    private BeforeLogsSendingEventArgs NotifySending(IList<CreateLogItemRequest> requests)
    {
        var args = new BeforeLogsSendingEventArgs(_service, _configuration, requests);
        ReportEventsSource.RaiseBeforeLogsSending(_reportEventsSource, this, args);
        return args;
    }

    private AfterLogsSentEventArgs NotifySent(IReadOnlyList<CreateLogItemRequest> requests)
    {
        var args = new AfterLogsSentEventArgs(_service, _configuration, requests);
        ReportEventsSource.RaiseAfterLogsSent(_reportEventsSource, this, args);
        return args;
    }
}
