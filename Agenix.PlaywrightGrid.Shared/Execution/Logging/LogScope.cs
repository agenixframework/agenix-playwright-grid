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

using Agenix.PlaywrightGrid.Shared.Extensibility;
using Agenix.PlaywrightGrid.Shared.Extensibility.Commands.CommandArgs;

namespace Agenix.PlaywrightGrid.Shared.Execution.Logging;

internal class LogScope : BaseLogScope
{
    public LogScope(ILogContext logContext, IExtensionManager extensionManager, CommandsSource commandsSource,
        ILogScope root, ILogScope parent, string name) : base(logContext, extensionManager, commandsSource)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Log scope name cannot be null of empty.", nameof(name));
        }

        Root = root;
        Parent = parent;
        Name = name;

        CommandsSource.RaiseOnBeginScopeCommand(commandsSource, logContext, new LogScopeCommandArgs(this));
    }

    public override ILogScope Parent { get; }

    public override string Name { get; }

    public override void Dispose()
    {
        base.Dispose();

        CommandsSource.RaiseOnEndScopeCommand(_commandsSource, Context, new LogScopeCommandArgs(this));

        Context.Log = Parent;
    }
}
