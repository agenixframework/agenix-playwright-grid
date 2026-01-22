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
using Agenix.PlaywrightGrid.Shared.Extensibility;

namespace Agenix.PlaywrightGrid.Shared;

/// <summary>
///     Provides a static context to access the current test context and launch context.
/// </summary>
public static class Context
{
    private static readonly Lazy<CommandsSource> CommandsSource =
        new(() => new CommandsSource(ExtensionManager.Instance.CommandsListeners));

    private static readonly Lazy<ITestContext> _current = new(() =>
        new TestContext(ExtensionManager.Instance, CommandsSource.Value));

    private static readonly Lazy<ILaunchContext> _launch = new(() =>
        new LaunchContext(ExtensionManager.Instance, CommandsSource.Value));

    /// <summary>
    ///     Gets the current test context, allowing access to test-specific metadata
    ///     and the ability to log contextual messages or scopes.
    /// </summary>
    /// <remarks>
    ///     The property provides an instance of <see cref="ITestContext" /> that includes
    ///     capabilities such as modifying test metadata during execution and interacting
    ///     with logging functionality. This context is initialized lazily and is shared
    ///     across the execution.
    /// </remarks>
    /// <value>
    ///     An instance of <see cref="ITestContext" /> representing the current test context.
    /// </value>
    public static ITestContext Current => _current.Value;

    /// <summary>
    ///     Gets the launch context, providing functionalities to manage and configure
    ///     the execution environment and interaction with log contexts during the session lifecycle.
    /// </summary>
    /// <remarks>
    ///     This property retrieves an instance of <see cref="ILaunchContext" /> which serves
    ///     as the entry point to initialization and contextual logging capabilities during
    ///     a test execution's lifecycle. It leverages the <see cref="LaunchContext" /> class to
    ///     encapsulate the operational environment alongside command source management.
    /// </remarks>
    /// <value>
    ///     An instance of <see cref="ILaunchContext" /> representing the launch context of the current execution.
    /// </value>
    public static ILaunchContext Launch => _launch.Value;
}
