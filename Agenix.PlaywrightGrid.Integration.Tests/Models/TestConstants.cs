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

namespace Agenix.PlaywrightGrid.Integration.Tests.Models;

/// <summary>
///     Constants used throughout integration tests.
///     Provides consistent values for project keys, API keys, status enums, and other test data.
/// </summary>
public static class TestConstants
{
    /// <summary>
    ///     Default project key used in tests.
    /// </summary>
    public const string DefaultProjectKey = "test_project";

    /// <summary>
    ///     Default owner API key used for launch creation.
    /// </summary>
    public const string DefaultOwnerApiKey = "test-api-key";

    /// <summary>
    ///     Default test user ID.
    /// </summary>
    public const string DefaultUserId = "test-user";

    /// <summary>
    ///     Default test username.
    /// </summary>
    public const string DefaultUsername = "testuser";

    /// <summary>
    ///     Default API key name.
    /// </summary>
    public const string DefaultApiKeyName = "integration-test";

    /// <summary>
    ///     Default browser label key for tests.
    /// </summary>
    public const string DefaultLabelKey = "AppB:Chromium:UAT";

    /// <summary>
    ///     Session status constants for test items.
    ///     Represents the browser/infrastructure lifecycle.
    /// </summary>
    public static class SessionStatus
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Completed = "Completed";
        public const string Stopped = "Stopped";
        public const string AutoStopped = "AutoStopped";
        public const string Aborted = "Aborted";
    }

    /// <summary>
    ///     Computed status constants for test items.
    ///     Represents the test execution outcome.
    /// </summary>
    public static class ComputedStatus
    {
        public const string InProgress = "InProgress";
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string Skipped = "Skipped";
        public const string Timedout = "Timedout";
        public const string Cancelled = "Cancelled";
        public const string Errored = "Errored";
    }

    /// <summary>
    ///     Launch status constants.
    /// </summary>
    public static class LaunchStatus
    {
        public const string InProgress = "InProgress";
        public const string Finished = "Finished";
        public const string Failed = "Failed";
        public const string Stopped = "Stopped";
    }

    /// <summary>
    ///     Test item type constants.
    /// </summary>
    public static class ItemType
    {
        public const string Test = "Test";
        public const string Step = "Step";
        public const string Suite = "Suite";
        public const string Scenario = "Scenario";
        public const string Story = "Story";
        public const string BeforeTest = "BeforeTest";
        public const string AfterTest = "AfterTest";
        public const string BeforeMethod = "BeforeMethod";
        public const string AfterMethod = "AfterMethod";
        public const string BeforeClass = "BeforeClass";
        public const string AfterClass = "AfterClass";
        public const string BeforeSuite = "BeforeSuite";
        public const string AfterSuite = "AfterSuite";
    }

    /// <summary>
    ///     History matrix status aggregation constants.
    ///     These are the status values returned by the history matrix functions.
    /// </summary>
    public static class HistoryStatus
    {
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string InProgress = "InProgress";
        public const string Skipped = "Skipped";
        public const string Mixed = "Mixed";
        public const string Empty = "Empty";
    }
}
