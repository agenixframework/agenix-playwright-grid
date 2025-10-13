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
using Serilog.Formatting.Display;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Custom Serilog sink that buffers log events by OperationId and renders them as visual chunks
///     with box-drawing characters when the operation completes.
/// </summary>
public sealed class ChunkedConsoleSink(
string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:l}{NewLine}{Exception}",
int maxEventsPerChunk = 1000,
int maxAgeSeconds = 60)
: ChunkedSinkBase(maxEventsPerChunk, maxAgeSeconds)
{
    private readonly MessageTemplateTextFormatter _formatter = new(outputTemplate);
    private static readonly object ConsoleLock = new();

    protected override void WriteLogEvent(LogEvent logEvent)
    {
        var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        lock (ConsoleLock)
        {
            Console.Write(writer.ToString());
        }
    }

    protected override void WriteLogEvents(IEnumerable<LogEvent> logEvents)
    {
        var mainWriter = new StringWriter();
        foreach (var logEvent in logEvents)
        {
            _formatter.Format(logEvent, mainWriter);
        }
        var output = mainWriter.ToString();

        lock (ConsoleLock)
        {
            Console.Write(output);
        }
    }
}

/// <summary>
///     Extension methods for registering ChunkedConsoleSink with Serilog configuration.
/// </summary>
public static class ChunkedConsoleSinkExtensions
{
    /// <summary>
    ///     Writes log events to the console with chunking by OperationId.
    /// </summary>
    public static LoggerConfiguration ChunkedConsole(
        this LoggerSinkConfiguration sinkConfiguration,
        string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:l}{NewLine}{Exception}",
        int maxEventsPerChunk = 1000,
        int maxAgeSeconds = 60)
    {
        return sinkConfiguration.Sink(new ChunkedConsoleSink(outputTemplate, maxEventsPerChunk, maxAgeSeconds));
    }
}
