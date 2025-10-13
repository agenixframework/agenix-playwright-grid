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

using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Base class for custom Serilog sinks that buffer log events by OperationId and render them as chunks
///     when the operation completes.
/// </summary>
public abstract class ChunkedSinkBase : ILogEventSink, IDisposable
{
    private readonly ConcurrentDictionary<Guid, OperationBuffer> _buffers = new();
    private readonly Timer _cleanupTimer;
    private readonly object _lock = new();
    private readonly TimeSpan _maxAge;
    private readonly int _maxEventsPerChunk;
    private bool _disposed;

    protected ChunkedSinkBase(int maxEventsPerChunk, int maxAgeSeconds)
    {
        _maxEventsPerChunk = maxEventsPerChunk;
        _maxAge = TimeSpan.FromSeconds(maxAgeSeconds);

        // Cleanup timer runs every 10 seconds to flush old buffers
        _cleanupTimer = new Timer(CleanupOldBuffers, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cleanupTimer?.Dispose();

        // Flush all remaining buffers
        foreach (var kvp in _buffers)
        {
            lock (kvp.Value.Lock)
            {
                FlushBuffer(kvp.Value);
            }
        }

        _buffers.Clear();
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    public void Emit(LogEvent logEvent)
    {
        if (_disposed)
        {
            return;
        }

        // Check if this is an operation-related event
        // We use RootOperationId for grouping to keep nested operations together
        if (!logEvent.Properties.TryGetValue("RootOperationId", out var rootOperationIdProp))
        {
            // Not part of an operation - just write directly
            WriteLogEvent(logEvent);
            return;
        }

        if (rootOperationIdProp is not ScalarValue { Value: Guid rootOperationId })
        {
            WriteLogEvent(logEvent);
            return;
        }

        // Check if this is a ROOT operation end event
        var isOperationEnd = logEvent.Properties.TryGetValue("EventType", out var eventTypeProp)
                             && eventTypeProp is ScalarValue { Value: string and "OperationEnd" };

        var isRootOperation = logEvent.Properties.TryGetValue("IsRootOperation", out var isRootProp)
                              && isRootProp is ScalarValue { Value: bool and true };

        // Get or create a buffer for this operation tree
        OperationBuffer buffer;
        while (true)
        {
            buffer = _buffers.GetOrAdd(rootOperationId, _ => new OperationBuffer(rootOperationId, DateTimeOffset.UtcNow));

            lock (buffer.Lock)
            {
                // Verify the buffer is still in the dictionary.
                // If it was removed by CleanupOldBuffers or a concurrent Flush while we were waiting for the lock,
                // we must get/create a new one to avoid losing this event.
                if (_buffers.TryGetValue(rootOperationId, out var currentBuffer) && ReferenceEquals(buffer, currentBuffer))
                {
                    buffer.Events.Add(logEvent);

                    // Flush buffer if ROOT operation ended or buffer is full
                    if ((isOperationEnd && isRootOperation) || buffer.Events.Count >= _maxEventsPerChunk)
                    {
                        FlushBuffer(buffer);
                        _buffers.TryRemove(rootOperationId, out _);
                    }
                    break;
                }
            }
        }
    }

    protected abstract void WriteLogEvent(LogEvent logEvent);

    protected virtual void WriteLogEvents(IEnumerable<LogEvent> logEvents)
    {
        foreach (var logEvent in logEvents)
        {
            WriteLogEvent(logEvent);
        }
    }

    private void FlushBuffer(OperationBuffer buffer)
    {
        if (buffer.Events.Count == 0)
        {
            return;
        }

        // Use a global lock for writing to ensure chunks are not interleaved if multiple buffers flush simultaneously
        lock (_lock)
        {
            WriteLogEvents(buffer.Events);
        }

        buffer.Events.Clear();
    }

    private void CleanupOldBuffers(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var oldBuffers = _buffers
            .Where(kvp => now - kvp.Value.CreatedAt > _maxAge)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var operationId in oldBuffers)
        {
            if (_buffers.TryRemove(operationId, out var buffer))
            {
                lock (buffer.Lock)
                {
                    FlushBuffer(buffer);
                }
            }
        }
    }

    protected sealed class OperationBuffer(Guid operationId, DateTimeOffset createdAt)
    {
        public Guid OperationId { get; } = operationId;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public List<LogEvent> Events { get; } = [];
        public object Lock { get; } = new();
    }
}
