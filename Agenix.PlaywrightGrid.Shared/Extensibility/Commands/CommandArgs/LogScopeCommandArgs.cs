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

namespace Agenix.PlaywrightGrid.Shared.Extensibility.Commands.CommandArgs;

/// <summary>
///     Represents the arguments for a log scope command.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="LogScopeCommandArgs" /> class with the specified log scope.
/// </remarks>
/// <param name="logScope">The log scope.</param>
public class LogScopeCommandArgs(ILogScope logScope) : EventArgs
{

    /// <summary>
    ///     Gets the log scope.
    /// </summary>
    public ILogScope LogScope { get; } = logScope;
}
