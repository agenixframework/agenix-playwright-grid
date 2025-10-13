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

using System.Text.Json.Serialization;

namespace Agenix.PlaywrightGrid.Client.Abstractions.Models;

/// <summary>
///     Describes types of test items.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TestItemType>))]
public enum TestItemType
{
    /// <summary>
    ///     Represents a test suite.
    /// </summary>
    Suite,

    /// <summary>
    ///     Represents a test case.
    /// </summary>
    Test,

    /// <summary>
    ///     Represents a test step.
    /// </summary>
    Step,

    /// <summary>
    ///     Represents a BDD scenario.
    /// </summary>
    Scenario,

    /// <summary>
    ///     Represents a user story.
    /// </summary>
    Story,

    /// <summary>
    ///     Represents a before class setup.
    /// </summary>
    BeforeClass,

    /// <summary>
    ///     Represents an after class cleanup.
    /// </summary>
    AfterClass,

    /// <summary>
    ///     Represents an after method cleanup.
    /// </summary>
    AfterMethod,

    /// <summary>
    ///     Represents a before method setup.
    /// </summary>
    BeforeMethod,

    /// <summary>
    ///     Represents a before suite setup.
    /// </summary>
    BeforeSuite,

    /// <summary>
    ///     Represents an after suite cleanup.
    /// </summary>
    AfterSuite,

    /// <summary>
    ///     Represents a before test setup.
    /// </summary>
    BeforeTest,

    /// <summary>
    ///     Represents an after test cleanup.
    /// </summary>
    AfterTest
}
