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

public class LaunchReporter : ILaunchReporter
{
    private readonly bool _asyncReporting;
    private readonly IConfiguration _configuration;
    private readonly IExtensionManager _extensionManager;

    private readonly bool _isExternalLaunchId;

    private readonly object _lockObj = new();
    private readonly ReportEventsSource _reportEventsSource;
    private readonly IRequestExecutor _requestExecuter;
    private readonly IClientService _service;

    private LaunchInfo _launchInfo;

    private LogsReporter _logsReporter;

    public LaunchReporter(IClientService service, IConfiguration configuration, IRequestExecutor requestExecuter,
        IExtensionManager extensionManager)
    {
        _service = service;

        if (configuration != null)
        {
            _configuration = configuration;
        }
        else
        {
            var configurationDirectory = AppContext.BaseDirectory;
            _configuration = new ConfigurationBuilder().AddDefaults(configurationDirectory).Build();
        }

        _asyncReporting = _configuration.GetValue(ConfigurationPath.AsyncReporting, false);
        _requestExecuter = requestExecuter ?? new RequestExecutorFactory(_configuration).Create();

        _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));

        _reportEventsSource = new ReportEventsSource();

        if (extensionManager.ReportEventObservers != null)
        {
            foreach (var reportEventObserver in extensionManager.ReportEventObservers)
            {
                try
                {
                    reportEventObserver.Initialize(_reportEventsSource);
                }
                catch (Exception initExp)
                {
                    TraceLogger.Error(
                        $"Unhandled exception while initializing of {reportEventObserver.GetType().FullName}: {initExp}");
                }
            }

            NotifyInitializing();
        }

        // identify whether launch is already started by any external system
        var externalLaunchUuid = _configuration.GetValue<string>("Launch:Id", null);
        if (externalLaunchUuid != null)
        {
            _isExternalLaunchId = true;

            _launchInfo = new LaunchInfo { Uuid = externalLaunchUuid };
        }
    }

    private ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<LaunchReporter>();
    public ILaunchReporterInfo Info => _launchInfo;

    public ILaunchStatisticsCounter StatisticsCounter { get; } = new LaunchStatisticsCounter();

    public Task StartTask { get; private set; }

    public void Start(StartLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TraceLogger.Verbose(
            $"Scheduling request to start new '{request.Name}' launch in {GetHashCode()} proxy instance");

        if (StartTask != null)
        {
            var exp = new InsufficientExecutionStackException("The launch is already scheduled for starting.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        if (!_isExternalLaunchId)
        {
            if (_configuration.GetValue("Launch:Rerun", false))
            {
                request.IsRerun = true;

                request.RerunOfLaunchUuid = _configuration.GetValue<string>("Launch:RerunOf", null);
            }

            // start new launch item or rerun existing
            StartTask = Task.Run(async () =>
            {
                NotifyStarting(request);

                var launch = await _requestExecuter
                    .ExecuteAsync(() => _asyncReporting
                            ? _service.AsyncLaunch.StartAsync(request)
                            : _service.Launch.StartAsync(request), null, null,
                        $"Starting new '{request.Name}' launch...")
                    .ConfigureAwait(false);

                _launchInfo = new LaunchInfo { Uuid = launch.Uuid, Name = request.Name, StartTime = request.StartTime };

                NotifyStarted();
            });
        }
        else
        {
            // get launch info
            StartTask = Task.Run(async () =>
            {
                var launch = await _requestExecuter.ExecuteAsync(() => _service.Launch.GetAsync(Guid.Parse(Info.Uuid)),
                    null, null, $"Getting existing launch by '{Info.Uuid}' uuid...").ConfigureAwait(false);

                _launchInfo = new LaunchInfo { Uuid = launch.Uuid, Name = launch.Name, StartTime = launch.StartTime };
            });
        }
    }

    public Task FinishTask { get; private set; }

    public void Finish(FinishLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TraceLogger.Verbose($"Scheduling request to finish launch in {GetHashCode()} proxy instance");

        if (StartTask == null)
        {
            var exp = new InsufficientExecutionStackException(
                "The launch wasn't scheduled for starting to finish it properly.");
            TraceLogger.Error(exp.ToString());
            throw exp;
        }

        if (FinishTask != null)
        {
            var exp = new InsufficientExecutionStackException("The launch is already scheduled for finishing.");
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

        FinishTask = Task.Factory.ContinueWhenAll([.. dependentTasks], async dts =>
        {
            if (StartTask.IsFaulted || StartTask.IsCanceled)
            {
                var exp = new Exception("Cannot finish launch due starting launch failed.", StartTask.Exception);

                if (StartTask.IsCanceled)
                {
                    exp = new Exception("Cannot finish launch due timeout while starting it.");
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
                            errors.Add(new Exception("Cannot finish launch due timeout while finishing test item."));
                        }
                    }

                    var exp = new AggregateException("Cannot finish launch due finishing of child items failed.",
                        errors);
                    TraceLogger.Error(exp.ToString());
                    throw exp;
                }
            }

            if (!_isExternalLaunchId)
            {
                NotifyFinishing(request);

                if (_asyncReporting)
                {
                    var launchFinishedResponse = await _requestExecuter
                        .ExecuteAsync(() => _service.AsyncLaunch.FinishAsync(Info.Uuid, request), null, null,
                            $"Finishing '{Info.Name}' launch...")
                        .ConfigureAwait(false);

                    _launchInfo.FinishTime = request.EndTime;
                    _launchInfo.Url = launchFinishedResponse.Link;
                }
                else
                {
                    await _requestExecuter
                        .ExecuteAsync(() => _service.Launch.FinishAsync(Guid.Parse(Info.Uuid), request), null, null,
                            $"Finishing '{Info.Name}' launch...")
                        .ConfigureAwait(false);

                    _launchInfo.FinishTime = request.EndTime;
                    // MessageResponse doesn't have Link property
                }

                NotifyFinished();
            }
        }, TaskContinuationOptions.PreferFairness).Unwrap();
    }

    public IList<ITestReporter> ChildTestReporters { get; private set; }

    public ITestReporter StartChildTestReporter(StartTestItemRequest request)
    {
        var newTestNode = new TestReporter(_service, _configuration, this, null, _requestExecuter, _extensionManager,
            _reportEventsSource);
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

    public void Log(CreateLogItemRequest createLogItemRequest)
    {
        if (StartTask == null)
        {
            var exp = new InsufficientExecutionStackException(
                "The launch wasn't scheduled for starting to add log messages.");
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
                    var logRequestAmender = new LaunchLogRequestAmender(this);

                    var logsBatchCapacity = _configuration.GetValue(ConfigurationPath.LogsBatchCapacity, 20);

                    _logsReporter = new LogsReporter(this, _service, _configuration, _extensionManager,
                        _requestExecuter, logRequestAmender, _reportEventsSource, logsBatchCapacity);
                }
            }

            _logsReporter.Log(createLogItemRequest);
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

    private LaunchInitializingEventArgs NotifyInitializing()
    {
        var args = new LaunchInitializingEventArgs(_service, _configuration);
        ReportEventsSource.RaiseLaunchInitializing(_reportEventsSource, this, args);
        return args;
    }

    private BeforeLaunchStartingEventArgs NotifyStarting(StartLaunchRequest request)
    {
        var args = new BeforeLaunchStartingEventArgs(_service, _configuration, request);
        ReportEventsSource.RaiseBeforeLaunchStarting(_reportEventsSource, this, args);
        return args;
    }

    private AfterLaunchStartedEventArgs NotifyStarted()
    {
        var args = new AfterLaunchStartedEventArgs(_service, _configuration);
        ReportEventsSource.RaiseAfterLaunchStarted(_reportEventsSource, this, args);
        return args;
    }

    private BeforeLaunchFinishingEventArgs NotifyFinishing(FinishLaunchRequest request)
    {
        var args = new BeforeLaunchFinishingEventArgs(_service, _configuration, request);
        ReportEventsSource.RaiseBeforeLaunchFinishing(_reportEventsSource, this, args);
        return args;
    }

    private AfterLaunchFinishedEventArgs NotifyFinished()
    {
        var args = new AfterLaunchFinishedEventArgs(_service, _configuration);
        ReportEventsSource.RaiseAfterLaunchFinished(_reportEventsSource, this, args);
        return args;
    }
}
