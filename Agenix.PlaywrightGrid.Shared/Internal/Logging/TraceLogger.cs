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

using System.Diagnostics;
using System.Globalization;

namespace Agenix.PlaywrightGrid.Shared.Internal.Logging;

/// <inheritdoc />
internal class TraceLogger(TraceSource traceSource) : ITraceLogger
{
    private readonly string _appDomainFriendlyName = AppDomain.CurrentDomain.FriendlyName;

    private readonly int _appDomainId = AppDomain.CurrentDomain.Id;
    private readonly TraceSource _traceSource = traceSource;

    public void Info(string message)
    {
        Message(TraceEventType.Information, message);
    }

    public void Verbose(string message)
    {
        Message(TraceEventType.Verbose, message);
    }

    public void Error(string message)
    {
        Message(TraceEventType.Error, message);
    }

    public void Warn(string message)
    {
        Message(TraceEventType.Warning, message);
    }

    private void Message(TraceEventType eventType, string message)
    {
        var formattedMessage = string.Format("{0} : {1}-{2} : {3}",
            DateTime.Now.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture), _appDomainId,
            _appDomainFriendlyName, message);
        _traceSource.TraceEvent(eventType, 0, formattedMessage);
    }
}
