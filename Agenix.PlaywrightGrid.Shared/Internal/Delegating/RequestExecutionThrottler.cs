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

using Agenix.PlaywrightGrid.Shared.Internal.Logging;

namespace Agenix.PlaywrightGrid.Shared.Internal.Delegating;

/// <inheritdoc />
public class RequestExecutionThrottler : IRequestExecutionThrottler, IDisposable
{
    private readonly SemaphoreSlim _concurrentAwaiter;

    private int _waitingThreads;

    /// <summary>
    ///     Initializes new instance of <see cref="RequestExecutionThrottler" />
    /// </summary>
    /// <param name="maxConcurrentRequests">Limit maximum number of concurrent requests.</param>
    public RequestExecutionThrottler(int maxConcurrentRequests)
    {
        if (maxConcurrentRequests < 1)
        {
            throw new ArgumentException("Maximum concurrent requests should be at least 1.",
                nameof(maxConcurrentRequests));
        }

        MaxCapacity = maxConcurrentRequests;

        _concurrentAwaiter = new SemaphoreSlim(maxConcurrentRequests);
    }

    private ITraceLogger TraceLogger { get; } = TraceLogManager.Instance.GetLogger<RequestExecutionThrottler>();

    /// <summary>
    ///     Releases all resources used by RequestExecutionThrottler.
    /// </summary>
    public void Dispose()
    {
        _concurrentAwaiter.Dispose();
    }

    /// <inheritdoc />
    public int MaxCapacity { get; }

    /// <inheritdoc />
    public async Task ReserveAsync()
    {
        TraceLogger.Verbose(
            $"Awaiting free executor. Available: {_concurrentAwaiter.CurrentCount}, waiting: {_waitingThreads}");

        Interlocked.Increment(ref _waitingThreads);

        await _concurrentAwaiter.WaitAsync().ConfigureAwait(false);

        TraceLogger.Verbose($"Executor is reserved. Available: {_concurrentAwaiter.CurrentCount}");
    }

    /// <inheritdoc />
    public void Release()
    {
        var previousCount = _concurrentAwaiter.Release();

        Interlocked.Decrement(ref _waitingThreads);

        TraceLogger.Verbose($"Executor is released. Available: {previousCount + 1}, waiting: {_waitingThreads}");
    }
}
