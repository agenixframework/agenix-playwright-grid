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
using Agenix.PlaywrightGrid.Client.Abstractions.Requests;

namespace Agenix.PlaywrightGrid.Shared.Execution.Logging;

/// <summary>
///     Provides extension methods for converting log messages to log item requests.
/// </summary>
public static class LogMessageExtensions
{
    /// <summary>
    ///     Converts a log message to a log item request.
    /// </summary>
    /// <param name="logMessage">The log message to convert.</param>
    /// <returns>A log item request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the log message is null.</exception>
    public static CreateLogItemRequest ConvertToRequest(this ILogMessage logMessage)
    {
        if (logMessage == null)
        {
            throw new ArgumentNullException(nameof(logMessage), "Cannot convert nullable log message object.");
        }

        string logLevel = logMessage.Level switch
        {
            LogMessageLevel.Debug => "DEBUG",
            LogMessageLevel.Error => "ERROR",
            LogMessageLevel.Fatal => "FATAL",
            LogMessageLevel.Info => "INFO",
            LogMessageLevel.Trace => "TRACE",
            LogMessageLevel.Warning => "WARN",
            _ => throw new Exception(string.Format("Unknown {0} level of log message.", logMessage.Level)),
        };
        var logRequest = new CreateLogItemRequest
        {
            Text = logMessage.Message,
            Time = logMessage.Time,
            Level = logLevel
        };

        if (logMessage.Attachment != null)
        {
            logRequest.Attach = new LogItemAttach
            {
                MimeType = logMessage.Attachment.MimeType,
                DataBase64 = Convert.ToBase64String(logMessage.Attachment.Data)
            };
        }

        return logRequest;
    }
}
