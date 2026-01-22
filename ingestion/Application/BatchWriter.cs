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
using Agenix.PlaywrightGrid.Shared;
using Agenix.PlaywrightGrid.Shared.Logging;

namespace IngestionService.Application;

/// <summary>
///     Generic batch accumulator that flushes based on size or time thresholds.
///     Thread-safe implementation using SemaphoreSlim.
/// </summary>
public sealed class BatchWriter<T> : IBatchWriter<T>, IDisposable
{
    private readonly List<T> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger _logger;
    private readonly ChunkedLogger<BatchWriter<T>>? _chunkedLogger;
    private readonly TimeSpan _maxBatchAge;
    private readonly int _maxBatchSize;
    private readonly Func<List<T>, CancellationToken, Task> _writeFunc;
    private DateTime _batchStartTime = DateTime.UtcNow;
    private bool _disposed;

    public BatchWriter(
        Func<List<T>, CancellationToken, Task> writeFunc,
        int maxBatchSize,
        TimeSpan maxBatchAge,
        ILogger logger,
        ChunkedLogger<BatchWriter<T>>? chunkedLogger = null)
    {
        _writeFunc = writeFunc;
        _maxBatchSize = maxBatchSize;
        _maxBatchAge = maxBatchAge;
        _logger = logger;
        _chunkedLogger = chunkedLogger;

        // Timer checks every 100ms if batch is old enough
        _flushTimer = new Timer(async _ => await CheckAndFlushAsync(), null, TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));
    }

    public async Task AddAsync(T item, CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_buffer.Count == 0)
            {
                _batchStartTime = DateTime.UtcNow;
            }

            _buffer.Add(item);

            // Flush if size threshold reached
            if (_buffer.Count >= _maxBatchSize)
            {
                await FlushInternalAsync(ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            await FlushInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _flushTimer.Dispose();
        _lock.Wait();
        try
        {
            if (_buffer.Count > 0)
            {
                _logger.LogWarning("Disposing BatchWriter with {Count} unflushed items", _buffer.Count);
            }
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }

    private async Task CheckAndFlushAsync()
    {
        if (_disposed)
        {
            return;
        }

        var acquired = await _lock.WaitAsync(0);
        if (!acquired)
        {
            return;
        }

        try
        {
            var age = DateTime.UtcNow - _batchStartTime;
            if (_buffer.Count > 0 && age >= _maxBatchAge)
            {
                await FlushInternalAsync(CancellationToken.None);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task FlushInternalAsync(CancellationToken ct)
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        using var op = _chunkedLogger?.BeginOperation(
            "FlushBatch",
            inputs: new Dictionary<string, object>
            {
                ["itemType"] = typeof(T).Name,
                ["batchSize"] = _buffer.Count
            });

        _chunkedLogger?.LogMilestone(
            EventCodes.Ingestion.BatchStarted,
            "Flushing {Count} {ItemType} items", _buffer.Count, typeof(T).Name);

        var batch = _buffer.ToList();
        _buffer.Clear();
        _batchStartTime = DateTime.UtcNow;

        var sw = Stopwatch.StartNew();
        try
        {
            await _writeFunc(batch, ct);
            _logger.LogDebug("Flushed batch of {Count} items in {Ms}ms", batch.Count, sw.ElapsedMilliseconds);

            _chunkedLogger?.LogMilestone(
                EventCodes.Ingestion.BatchCompleted,
                "Successfully flushed {Count} {ItemType} items", batch.Count, typeof(T).Name);

            op?.SetOutputs(new Dictionary<string, object>
            { ["flushed"] = true, ["durationMs"] = sw.ElapsedMilliseconds });
            op?.Complete();
        }
        catch (Exception ex)
        {
            op?.Fail(ex, ErrorType.Unexpected);
            _logger.LogError(ex, "Failed to flush batch of {Count} items", batch.Count);
            throw;
        }
    }
}
