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

using Agenix.PlaywrightGrid.Shared.Execution;
using Agenix.PlaywrightGrid.Shared.Extensibility.Commands.CommandArgs;

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Commands;

/// <summary>
///     Represents a source of test commands.
/// </summary>
public interface ITestCommandsSource
{
    /// <summary>
    ///     Occurs when a test command to get test attributes is raised.
    /// </summary>
    event TestCommandHandler<TestAttributesCommandArgs> OnGetTestAttributes;

    /// <summary>
    ///     Occurs when a test command to add test attributes is raised.
    /// </summary>
    event TestCommandHandler<TestAttributesCommandArgs> OnAddTestAttributes;

    /// <summary>
    ///     Occurs when a test command to remove test attributes is raised.
    /// </summary>
    event TestCommandHandler<TestAttributesCommandArgs> OnRemoveTestAttributes;
}

/// <summary>
///     Represents a delegate for handling test commands.
/// </summary>
/// <typeparam name="TCommandArgs">The type of the command arguments.</typeparam>
/// <param name="testContext">The test context.</param>
/// <param name="args">The command arguments.</param>
public delegate void TestCommandHandler<TCommandArgs>(ITestContext testContext, TCommandArgs args);
