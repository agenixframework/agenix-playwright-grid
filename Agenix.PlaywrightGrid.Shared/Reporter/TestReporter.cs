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

using Agenix.PlaywrightGrid.Client.Abstractions.Requests;
using Agenix.PlaywrightGrid.Shared.Configuration;
using Agenix.PlaywrightGrid.Shared.Extensibility;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Internal.Delegating;
using Agenix.PlaywrightGrid.Shared.Internal.Logging;
using Agenix.PlaywrightGrid.Shared.Reporter.Statistics;
using ReportPortal.Client.Abstractions;

namespace Agenix.PlaywrightGrid.Shared.Reporter;

public class TestReporter : ITestReporter
{
    private readonly bool _asyncReporting;
    private readonly IConfiguration _configuration;
    private readonly IExtensionManager _extensionManager;

    private readonly object _lockObj = new();

    private readonly ReportEventsSource _reportEventsSource;
    private readonly IRequestExecutor _requestExecuter;
    private readonly IClientService _service;

    private LogsReporter _logsReporter;

    private TestInfo _testInfo;

    public TestReporter(IClientService service, IConfiguration configuration, ILaunchReporter launchReporter,
        ITestReporter parentTestReporter, IRequestExecutor requestExecuter,
        IExtensionManager extensionManager, ReportEventsSource reportEventNotifier)
    {
        _service = service;
        _configuration = configuration;
        _requestExecuter = requestExecuter;
        _extensionManager = extensionManager;
        _reportEventsSource = reportEventNotifier;
        _asyncReporting = _configuration.GetValue(ConfigurationPath.AsyncReporting, false);

        LaunchReporter = launchReporter;
        ParentTestReporter = parentTestReporter;
    }

    private static ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<TestReporter>();
    public ITestReporterInfo Info => _testInfo;

    public ILaunchReporter LaunchReporter { get; }

    public ITestReporter ParentTestReporter { get; }

    public Task StartTask { get; private set; }

    public void Start(StartTestItemRequest startTestItemRequest)
    {
        ArgumentNullException.ThrowIfNull(startTestItemRequest);

        if (StartTask != null)
        {
            var exp = new InsufficientExecutionStackException("The test item is already scheduled for starting.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        TraceLogger.Verbose($"Scheduling request to start test item in {GetHashCode()} proxy instance");

        var parentStartTask = ParentTestReporter?.StartTask ?? LaunchReporter.StartTask;

        StartTask = parentStartTask.ContinueWith(async pt =>
        {
            if (pt.IsFaulted || pt.IsCanceled)
            {
                var exp = new Exception("Cannot start test item due parent failed to start.", pt.Exception);

                if (pt.IsCanceled)
                {
                    exp = new Exception("Cannot start test item due timeout while starting parent.");
                }

                TraceLogger.Error(exp.ToString());
                throw exp;
            }

            startTestItemRequest.LaunchUuid = LaunchReporter.Info.Uuid;

            if (ParentTestReporter == null)
            {
                NotifyStarting(startTestItemRequest);

                var testModel = await _requestExecuter
                    .ExecuteAsync(() => _asyncReporting
                            ? _service.AsyncTestItem.StartAsync(startTestItemRequest)
                            : _service.TestItem.StartAsync(startTestItemRequest),
                        null,
                        LaunchReporter.StatisticsCounter.StartTestItemStatisticsCounter,
                        $"Starting new '{startTestItemRequest.Name}' test item...")
                    .ConfigureAwait(false);

                _testInfo = new TestInfo
                {
                    Uuid = testModel.Uuid,
                    Name = startTestItemRequest.Name,
                    StartTime = startTestItemRequest.StartTime
                };

                NotifyStarted();
            }
            else
            {
                NotifyStarting(startTestItemRequest);

                var testModel = await _requestExecuter
                    .ExecuteAsync(() => _asyncReporting
                            ? _service.AsyncTestItem.StartAsync(startTestItemRequest)
                            : _service.TestItem.StartAsync(startTestItemRequest),
                        null,
                        LaunchReporter.StatisticsCounter.StartTestItemStatisticsCounter,
                        $"Starting new '{startTestItemRequest.Name}' test item...")
                    .ConfigureAwait(false);

                _testInfo = new TestInfo
                {
                    Uuid = testModel.Uuid,
                    Name = startTestItemRequest.Name,
                    StartTime = startTestItemRequest.StartTime
                };

                NotifyStarted();
            }

            _testInfo.StartTime = startTestItemRequest.StartTime;
        }, TaskContinuationOptions.PreferFairness).Unwrap();
    }

    public Task FinishTask { get; private set; }

    public void Finish(FinishTestItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TraceLogger.Verbose($"Scheduling request to finish test item in {GetHashCode()} proxy instance");

        if (StartTask == null)
        {
            var exp = new InsufficientExecutionStackException(
                "The test item wasn't scheduled for starting to finish it properly.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        if (FinishTask != null)
        {
            var exp = new InsufficientExecutionStackException("The test item is already scheduled for finishing.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        var dependentTasks = new List<Task> { StartTask };

        if (_logsReporter != null)
        {
            _logsReporter.Finish();
            dependentTasks.Add(_logsReporter.ProcessingTask);
        }

        if (ChildTestReporters != null)
        {
            var childTestReporterFinishTasks = ChildTestReporters.Select(tn => tn.FinishTask);
            if (childTestReporterFinishTasks.Contains(null))
            {
                throw new InsufficientExecutionStackException(
                    "Some of child test item(s) are not scheduled to finish yet.");
            }

            dependentTasks.AddRange(childTestReporterFinishTasks);
        }

        FinishTask = Task.Factory.ContinueWhenAll([.. dependentTasks], async a =>
        {
            if (StartTask.IsFaulted || StartTask.IsCanceled)
            {
                var exp = new Exception("Cannot finish test item due starting item failed.", StartTask.Exception);

                if (StartTask.IsCanceled)
                {
                    exp = new Exception("Cannot finish test item due timeout while starting it.");
                }

                TraceLogger.Error(exp.ToString());
                throw exp;
            }

            if (ChildTestReporters != null)
            {
                var failedChildTestReporters =
                    ChildTestReporters.Where(ctr => ctr.FinishTask.IsFaulted || ctr.FinishTask.IsCanceled);
                if (failedChildTestReporters.Any())
                {
                    var errors = new List<Exception>();
                    foreach (var failedChildTestReporter in failedChildTestReporters)
                    {
                        if (failedChildTestReporter.FinishTask.IsFaulted)
                        {
                            errors.Add(failedChildTestReporter.FinishTask.Exception);
                        }
                        else if (failedChildTestReporter.FinishTask.IsCanceled)
                        {
                            errors.Add(new Exception("Timeout while finishing child test item."));
                        }
                    }

                    var exp = new AggregateException("Cannot finish test item due finishing of child items failed.",
                        errors);
                    TraceLogger.Error(exp.ToString());
                    throw exp;
                }
            }

            NotifyFinishing(request);

            await _requestExecuter
                .ExecuteAsync(() => _asyncReporting
                        ? _service.AsyncTestItem.FinishAsync(Info.Uuid, request)
                        : _service.TestItem.FinishAsync(Guid.Parse(Info.Uuid), request),
                    null,
                    LaunchReporter.StatisticsCounter.FinishTestItemStatisticsCounter,
                    $"Finishing '{Info.Name}' test item with '{request.Status}' status...")
                .ConfigureAwait(false);

            _testInfo.FinishTime = request.EndTime;
            _testInfo.Status = request.Status;

            NotifyFinished();
        }, TaskContinuationOptions.PreferFairness).Unwrap();
    }

    public IList<ITestReporter> ChildTestReporters { get; private set; }

    public ILaunchStatisticsCounter StatisticsCounter => LaunchReporter.StatisticsCounter;

    public ITestReporter StartChildTestReporter(StartTestItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TraceLogger.Verbose(
            $"Scheduling request to start new '{request.Name}' test item in {GetHashCode()} proxy instance");

        var newTestNode = new TestReporter(_service, _configuration, LaunchReporter, this, _requestExecuter,
            _extensionManager, _reportEventsSource);
        newTestNode.Start(request);

        lock (_lockObj)
        {
            if (ChildTestReporters == null)
            {
                lock (_lockObj)
                {
                    ChildTestReporters = [];
                }
            }

            ChildTestReporters.Add(newTestNode);
        }

        return newTestNode;
    }

    public void Log(CreateLogItemRequest request)
    {
        if (StartTask == null)
        {
            var exp = new InsufficientExecutionStackException(
                "The test item wasn't scheduled for starting to add log messages.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        if (StartTask.IsFaulted || StartTask.IsCanceled)
        {
            return;
        }

        if (FinishTask == null)
        {
            lock (_lockObj)
            {
                if (_logsReporter == null)
                {
                    var logRequestAmender = new TestLogRequestAmender(this);

                    var logsBatchCapacity = _configuration.GetValue(ConfigurationPath.LogsBatchCapacity, 20);

                    _logsReporter = new LogsReporter(this, _service, _configuration, _extensionManager,
                        _requestExecuter, logRequestAmender, _reportEventsSource, logsBatchCapacity);
                }
            }

            _logsReporter.Log(request);
        }
    }

    public void Sync()
    {
        _logsReporter?.Sync();

        if (FinishTask != null)
        {
            FinishTask.GetAwaiter().GetResult();
        }
        else
        {
            StartTask?.GetAwaiter().GetResult();
        }

        if (ChildTestReporters != null)
        {
            foreach (var testNode in ChildTestReporters)
            {
                testNode.Sync();
            }
        }
    }

    private BeforeTestStartingEventArgs NotifyStarting(StartTestItemRequest request)
    {
        var args = new BeforeTestStartingEventArgs(_service, _configuration, request);
        ReportEventsSource.RaiseBeforeTestStarting(_reportEventsSource, this, args);
        return args;
    }

    private AfterTestStartedEventArgs NotifyStarted()
    {
        var args = new AfterTestStartedEventArgs(_service, _configuration);
        ReportEventsSource.RaiseAfterTestStarted(_reportEventsSource, this, args);
        return args;
    }

    private BeforeTestFinishingEventArgs NotifyFinishing(FinishTestItemRequest request)
    {
        var args = new BeforeTestFinishingEventArgs(_service, _configuration, request);
        ReportEventsSource.RaiseBeforeTestFinishing(_reportEventsSource, this, args);
        return args;
    }

    private AfterTestFinishedEventArgs NotifyFinished()
    {
        var args = new AfterTestFinishedEventArgs(_service, _configuration);
        ReportEventsSource.RaiseAfterTestFinished(_reportEventsSource, this, args);
        return args;
    }
}
