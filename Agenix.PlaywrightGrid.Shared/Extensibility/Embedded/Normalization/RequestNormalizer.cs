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

using Agenix.PlaywrightGrid.Client.Abstractions.Models;
using Agenix.PlaywrightGrid.Shared.Converters;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents;
using Agenix.PlaywrightGrid.Shared.Extensibility.ReportEvents.EventArgs;
using Agenix.PlaywrightGrid.Shared.Reporter;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Embedded.Normalization;

/// <summary>
///     Report events observer which makes basic validation and normalization before sending http requests to server.
///     Examples:
///     - Care about self/parent start/finish time
///     - Limit long strings (name, attributes)
/// </summary>
public class RequestNormalizer : IReportEventsObserver
{
    // TODO: make it configurable
    internal const int MAX_LAUNCH_NAME_LENGTH = 256;
    internal const int MAX_TEST_ITEM_NAME_LENGTH = 1024;

    internal const int MAX_ATTRIBUTE_KEY_LENGTH = 128;
    internal const int MAX_ATTRIBUTE_VALUE_LENGTH = 128;

    /// <inheritdoc />
    public void Initialize(IReportEventsSource reportEventsSource)
    {
        reportEventsSource.OnBeforeLaunchStarting += ReportEventsSource_OnBeforeLaunchStarting;
        reportEventsSource.OnBeforeLaunchFinishing += ReportEventsSource_OnBeforeLaunchFinishing;
        reportEventsSource.OnBeforeTestStarting += ReportEventsSource_OnBeforeTestStarting;
        reportEventsSource.OnBeforeTestFinishing += ReportEventsSource_OnBeforeTestFinishing;
    }

    private void ReportEventsSource_OnBeforeLaunchStarting(ILaunchReporter launchReporter,
        BeforeLaunchStartingEventArgs args)
    {
        args.StartLaunchRequest.Name = StringTrimmer.Trim(args.StartLaunchRequest.Name, MAX_LAUNCH_NAME_LENGTH);

        NormalizeAttributes(args.StartLaunchRequest.Attributes);
    }

    private void ReportEventsSource_OnBeforeLaunchFinishing(ILaunchReporter launchReporter,
        BeforeLaunchFinishingEventArgs args)
    {
        if (args.FinishLaunchRequest.EndTime < launchReporter.Info.StartTime)
        {
            args.FinishLaunchRequest.EndTime = launchReporter.Info.StartTime;
        }
    }

    private void ReportEventsSource_OnBeforeTestStarting(ITestReporter testReporter, BeforeTestStartingEventArgs args)
    {
        var parentStartTime = testReporter.ParentTestReporter?.Info.StartTime ??
                              testReporter.LaunchReporter.Info.StartTime;

        if (args.StartTestItemRequest.StartTime < parentStartTime)
        {
            args.StartTestItemRequest.StartTime = parentStartTime;
        }

        args.StartTestItemRequest.Name = StringTrimmer.Trim(args.StartTestItemRequest.Name, MAX_TEST_ITEM_NAME_LENGTH);

        NormalizeAttributes(args.StartTestItemRequest.Attributes);
    }

    private void ReportEventsSource_OnBeforeTestFinishing(ITestReporter testReporter, BeforeTestFinishingEventArgs args)
    {
        if (args.FinishTestItemRequest.EndTime < testReporter.Info.StartTime)
        {
            args.FinishTestItemRequest.EndTime = testReporter.Info.StartTime;
        }

        NormalizeAttributes(args.FinishTestItemRequest.Attributes);

        args.FinishTestItemRequest.LaunchUuid = testReporter.LaunchReporter.Info.Uuid;
    }

    private static void NormalizeAttributes(IEnumerable<ItemAttribute> attributes)
    {
        if (attributes != null)
        {
            foreach (var attribute in attributes)
            {
                attribute.Key = StringTrimmer.Trim(attribute.Key, MAX_ATTRIBUTE_KEY_LENGTH);
                attribute.Value = StringTrimmer.Trim(attribute.Value, MAX_ATTRIBUTE_VALUE_LENGTH);
            }
        }
    }
}
