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

using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Custom Serilog sink that buffers log events by OperationId and writes them to a file in chunks.
/// </summary>
public sealed class ChunkedFileSink(
    ILogger internalLogger,
    int maxEventsPerChunk = 1000,
    int maxAgeSeconds = 60) : ChunkedSinkBase(maxEventsPerChunk, maxAgeSeconds)
{
    private readonly ILogger _internalLogger = internalLogger;

    protected override void WriteLogEvent(LogEvent logEvent)
    {
        _internalLogger.Write(logEvent);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _internalLogger is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
///     Extension methods for registering ChunkedFileSink with Serilog configuration.
/// </summary>
public static class ChunkedFileSinkExtensions
{
    /// <summary>
    ///     Writes log events to a file with chunking by OperationId.
    /// </summary>
    public static LoggerConfiguration ChunkedFile(
        this LoggerSinkConfiguration sinkConfiguration,
        string path,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        int? retainedFileCountLimit = 31,
        long? fileSizeLimitBytes = 1073741824,
        string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:l}{NewLine}{Exception}",
        int maxEventsPerChunk = 1000,
        int maxAgeSeconds = 60)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        var internalLogger = new LoggerConfiguration()
            .WriteTo.File(
                expandedPath,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: retainedFileCountLimit,
                fileSizeLimitBytes: fileSizeLimitBytes,
                outputTemplate: outputTemplate)
            .CreateLogger();

        return sinkConfiguration.Sink(new ChunkedFileSink(internalLogger, maxEventsPerChunk, maxAgeSeconds));
    }
}
