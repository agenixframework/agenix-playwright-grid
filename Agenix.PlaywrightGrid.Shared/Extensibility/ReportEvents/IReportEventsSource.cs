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

using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Reporter;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents;

/// <summary>
///     Represents the interface for a source of report events.
/// </summary>
public interface IReportEventsSource
{
    /// <summary>
    ///     Occurs when the launch is initializing.
    /// </summary>
    event LaunchEventHandler<LaunchInitializingEventArgs> OnLaunchInitializing;

    /// <summary>
    ///     Occurs before the launch is starting.
    /// </summary>
    event LaunchEventHandler<BeforeLaunchStartingEventArgs> OnBeforeLaunchStarting;

    /// <summary>
    ///     Occurs after the launch has started.
    /// </summary>
    event LaunchEventHandler<AfterLaunchStartedEventArgs> OnAfterLaunchStarted;

    /// <summary>
    ///     Occurs before the launch is finishing.
    /// </summary>
    event LaunchEventHandler<BeforeLaunchFinishingEventArgs> OnBeforeLaunchFinishing;

    /// <summary>
    ///     Occurs after the launch has finished.
    /// </summary>
    event LaunchEventHandler<AfterLaunchFinishedEventArgs> OnAfterLaunchFinished;

    /// <summary>
    ///     Occurs before a test is starting.
    /// </summary>
    event TestEventHandler<BeforeTestStartingEventArgs> OnBeforeTestStarting;

    /// <summary>
    ///     Occurs after a test has started.
    /// </summary>
    event TestEventHandler<AfterTestStartedEventArgs> OnAfterTestStarted;

    /// <summary>
    ///     Occurs before a test is finishing.
    /// </summary>
    event TestEventHandler<BeforeTestFinishingEventArgs> OnBeforeTestFinishing;

    /// <summary>
    ///     Occurs after a test has finished.
    /// </summary>
    event TestEventHandler<AfterTestFinishedEventArgs> OnAfterTestFinished;

    /// <summary>
    ///     Occurs before logs are sending.
    /// </summary>
    event LogsEventHandler<BeforeLogsSendingEventArgs> OnBeforeLogsSending;

    /// <summary>
    ///     Occurs after logs are sent.
    /// </summary>
    event LogsEventHandler<AfterLogsSentEventArgs> OnAfterLogsSent;
}

/// <summary>
///     Represents the delegate for handling launch events.
/// </summary>
/// <typeparam name="TEventArgs">The type of the event arguments.</typeparam>
/// <param name="launchReporter">The launch reporter.</param>
/// <param name="args">The event arguments.</param>
public delegate void LaunchEventHandler<TEventArgs>(ILaunchReporter launchReporter, TEventArgs args);

/// <summary>
///     Represents the delegate for handling test events.
/// </summary>
/// <typeparam name="TEventArgs">The type of the event arguments.</typeparam>
/// <param name="testReporter">The test reporter.</param>
/// <param name="args">The event arguments.</param>
public delegate void TestEventHandler<TEventArgs>(ITestReporter testReporter, TEventArgs args);

/// <summary>
///     Represents the delegate for handling logs events.
/// </summary>
/// <typeparam name="TEventArgs">The type of the event arguments.</typeparam>
/// <param name="logsReporter">The logs reporter.</param>
/// <param name="args">The event arguments.</param>
public delegate void LogsEventHandler<TEventArgs>(ILogsReporter logsReporter, TEventArgs args);
