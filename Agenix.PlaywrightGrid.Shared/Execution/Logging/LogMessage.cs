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

namespace Agenix.PlaywrightGrid.Shared.Execution.Logging;

/// <inheritdoc />
public class LogMessage : ILogMessage
{
    /// <summary>
    ///     Creates new instance of <see href="LogMessage" />
    /// </summary>
    /// <param name="message">Textual log event message.</param>
    public LogMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            throw new ArgumentException("Log message cannot be null or empty", nameof(message));
        }

        Message = message;
        Time = DateTime.UtcNow;
        Level = LogMessageLevel.Info;
    }

    /// <inheritdoc />
    public string Message { get; set; }

    /// <inheritdoc />
    public DateTime Time { get; set; }

    /// <inheritdoc />
    public LogMessageLevel Level { get; set; }

    /// <inheritdoc />
    public ILogMessageAttachment Attachment { get; set; }
}
