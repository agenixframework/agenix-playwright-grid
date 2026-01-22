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

using Agenix.PlaywrightGrid.Shared.Execution.Logging;
using Agenix.PlaywrightGrid.Shared.Extensibility;

namespace Agenix.PlaywrightGrid.Shared.Execution;

/// <summary>
///     Represents the context of a launch.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="LaunchContext" /> class.
/// </remarks>
/// <param name="extensionManager">The extension manager.</param>
/// <param name="commandsSource">The commands source.</param>
public class LaunchContext(IExtensionManager extensionManager, CommandsSource commandsSource) : ILaunchContext
{
    private readonly AsyncLocal<ILogScope> _activeLogScope = new();
    private readonly CommandsSource _commadsSource = commandsSource;
    private readonly IExtensionManager _extensionManager = extensionManager;
    private readonly AsyncLocal<ILogScope> _rootLogScope = new();

    private ILogScope RootScope
    {
        get
        {
            if (_rootLogScope.Value == null)
            {
                //TraceLogger.Info($"New log context identified, activating {typeof(RootLogScope).Name}");
                RootScope = new RootLogScope(this, _extensionManager, _commadsSource);
            }

            return _rootLogScope.Value;
        }
        set => _rootLogScope.Value = value;
    }

    /// <summary>
    ///     Gets or sets the current active LogScope which provides methods for logging.
    /// </summary>
    public ILogScope Log
    {
        get
        {
            if (_activeLogScope.Value == null)
            {
                Log = RootScope;
            }

            return _activeLogScope.Value;
        }
        set => _activeLogScope.Value = value;
    }
}
