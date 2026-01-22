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
using Agenix.PlaywrightGrid.Shared.MimeTypes;

namespace Agenix.PlaywrightGrid.Shared.Execution.Logging;

internal abstract class BaseLogScope(ILogContext logContext, IExtensionManager extensionManager, CommandsSource commandsSource) : ILogScope
{
    protected CommandsSource _commandsSource = commandsSource;
    protected IExtensionManager _extensionManager = extensionManager;

    public virtual string Id { get; } = Guid.NewGuid().ToString();

    public virtual ILogScope Parent { get; }

    public virtual ILogScope Root { get; protected set; }

    public virtual ILogContext Context { get; } = logContext;

    public virtual string Name { get; }

    public virtual DateTime BeginTime { get; } = DateTime.UtcNow;

    public virtual DateTime? EndTime { get; private set; }

    public virtual LogScopeStatus Status { get; set; } = LogScopeStatus.InProgress;

    public virtual ILogScope BeginScope(string name)
    {
        var logScope = new LogScope(Context, _extensionManager, _commandsSource, Root, this, name);

        Context.Log = logScope;

        return logScope;
    }

    public void Debug(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Debug;
        Message(logMessage);
    }

    public void Debug(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Debug;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Debug(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Debug;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public void Error(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Error;
        Message(logMessage);
    }

    public void Error(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Error;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Error(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Error;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public void Fatal(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Fatal;
        Message(logMessage);
    }

    public void Fatal(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Fatal;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Fatal(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Fatal;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public void Info(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Info;
        Message(logMessage);
    }

    public void Info(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Info;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Info(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Info;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public void Trace(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Trace;
        Message(logMessage);
    }

    public void Trace(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Trace;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Trace(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Trace;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public void Warn(string message)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Warning;
        Message(logMessage);
    }

    public void Warn(string message, string mimeType, byte[] content)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Warning;
        logMessage.Attachment = GetAttachFromContent(mimeType, content);
        Message(logMessage);
    }

    public void Warn(string message, FileInfo file)
    {
        var logMessage = GetDefaultLogRequest(message);
        logMessage.Level = LogMessageLevel.Warning;
        Message(GetLogMessageWithAttachmentFromFileInfo(logMessage, file));
    }

    public virtual void Message(ILogMessage log)
    {
        CommandsSource.RaiseOnLogMessageCommand(_commandsSource, Context, new LogMessageCommandArgs(this, log));
    }

    public virtual void Dispose()
    {
        EndTime = DateTime.UtcNow;

        if (Status == LogScopeStatus.InProgress)
        {
            Status = LogScopeStatus.Passed;
        }
    }

    protected static ILogMessage GetDefaultLogRequest(string text)
    {
        var logMessage = new LogMessage(text);

        return logMessage;
    }

    protected static ILogMessageAttachment GetAttachFromContent(string mimeType, byte[] content)
    {
        return new LogMessageAttachment(mimeType, content);
    }

    protected static ILogMessage GetLogMessageWithAttachmentFromFileInfo(ILogMessage message, FileInfo file)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(file);

            var contentType = MimeTypeMap.GetMimeType(file.Extension);

            using var fileStream = File.Open(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            var bytes = memoryStream.ToArray();

            message.Attachment = new LogMessageAttachment(contentType, bytes);
        }
        catch (Exception ex)
        {
            message.Message = $"{message.Message}\n> Couldn't read content of `{file?.FullName}` file. \n{ex}";
        }

        return message;
    }
}
