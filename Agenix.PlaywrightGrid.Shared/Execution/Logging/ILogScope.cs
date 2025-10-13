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

/// <summary>
///     Sends log messages to active logging scope.
/// </summary>
public interface ILogScope : IDisposable
{
    /// <summary>
    ///     Unique ID of current logging scope.
    /// </summary>
    string Id { get; }

    /// <summary>
    ///     Parent logging scope.
    /// </summary>
    ILogScope Parent { get; }

    /// <summary>
    ///     Root logging scope.
    /// </summary>
    ILogScope Root { get; }

    /// <summary>
    ///     Context which current logging scope belong to.
    /// </summary>
    ILogContext Context { get; }

    /// <summary>
    ///     Logical login scope name.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Time when loging scope began.
    /// </summary>
    DateTime BeginTime { get; }

    /// <summary>
    ///     Time when logging scope ended.
    /// </summary>
    DateTime? EndTime { get; }

    /// <summary>
    ///     Logging scope status.
    /// </summary>
    LogScopeStatus Status { get; set; }

    /// <summary>
    ///     Starts new logging scope beginning from active scope.
    /// </summary>
    /// <param name="name">A name of the scope.</param>
    /// <returns></returns>
    ILogScope BeginScope(string name);

    /// <summary>
    ///     Sends log message to current test context.
    /// </summary>
    /// <param name="log">Full model object for message</param>
    void Message(ILogMessage log);

    /// <summary>
    ///     Sends log message with "Info" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Info(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Info(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Info(string message, FileInfo file);

    /// <summary>
    ///     Sends log message with "Debug" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Debug(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Debug(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Debug(string message, FileInfo file);

    /// <summary>
    ///     Sends log message with "Trace" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Trace(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Trace(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Trace(string message, FileInfo file);

    /// <summary>
    ///     Sends log message with "Error" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Error(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Error(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Error(string message, FileInfo file);

    /// <summary>
    ///     Sends log message with "Fatal" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Fatal(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Fatal(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Fatal(string message, FileInfo file);

    /// <summary>
    ///     Sends log message with "Warn" level to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    void Warn(string message);

    /// <summary>
    ///     Sends binary content to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="mimeType">Mime type of content</param>
    /// <param name="content">Array of bytes</param>
    void Warn(string message, string mimeType, byte[] content);

    /// <summary>
    ///     Sends file to current test context.
    /// </summary>
    /// <param name="message">Text of the message</param>
    /// <param name="file">File on the disk</param>
    void Warn(string message, FileInfo file);
}
