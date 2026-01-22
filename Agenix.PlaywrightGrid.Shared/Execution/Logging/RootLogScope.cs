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

internal class RootLogScope(ILogContext logContext, IExtensionManager extensionManager, CommandsSource commandsSource) : BaseLogScope(logContext, extensionManager, commandsSource)
{
    public override LogScopeStatus Status { get => base.Status; set { } }

    public override ILogScope Root { get => this; protected set => base.Root = value; }

    public override void Message(ILogMessage logMessage)
    {
        CommandsSource.RaiseOnLogMessageCommand(_commandsSource, Context, new LogMessageCommandArgs(null, logMessage));
    }

    public override ILogScope BeginScope(string name)
    {
        var logScope = new LogScope(Context, _extensionManager, _commandsSource, this, null, name);

        Context.Log = logScope;

        return logScope;
    }
}
