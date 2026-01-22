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

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Dashboard.Services;

/// <summary>
/// Implementation of <see cref="IRequestDeduplicationService"/> that uses a concurrent dictionary
/// and lazy initialization to ensure each unique request is executed only once while in-flight.
/// </summary>
public sealed class RequestDeduplicationService : IRequestDeduplicationService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlightRequests = new();
    private readonly ILogger<RequestDeduplicationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestDeduplicationService"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostics.</param>
    public RequestDeduplicationService(ILogger<RequestDeduplicationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Expected key format: {httpMethod}:{endpoint} (e.g., GET:/api/launches/123).
    /// </remarks>
    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> operation)
    {
        // Try to get or add a Lazy wrapper for the task.
        // Lazy ensures that even if GetOrAdd calls the factory multiple times (under contention),
        // only one Lazy object is stored and only one operation is actually started when .Value is accessed.
        var lazyTask = _inFlightRequests.GetOrAdd(key, k =>
        {
            _logger.LogInformation("[RequestDedup] Creating new operation for {Key}", k);

            return new Lazy<Task<object?>>(async () =>
            {
                try
                {
                    var result = await operation();
                    _logger.LogDebug("[RequestDedup] Operation {Key} completed successfully", k);
                    return (object?)result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RequestDedup] Operation {Key} failed: {Message}", k, ex.Message);
                    throw;
                }
                finally
                {
                    // Ensure the key is removed from the dictionary after completion (success or failure)
                    // so that future requests can be made.
                    if (_inFlightRequests.TryRemove(k, out _))
                    {
                        _logger.LogDebug("[RequestDedup] Removed {Key} from in-flight store after completion", k);
                    }
                }
            });
        });

        // Detect if we are using an existing in-flight request
        if (lazyTask.IsValueCreated)
        {
            _logger.LogInformation("[RequestDedup] Duplicate request detected for {Key}. Awaiting existing task.", key);
        }

        var result = await lazyTask.Value;
        return (T)result!;
    }

    /// <inheritdoc />
    public void Clear(string key)
    {
        if (_inFlightRequests.TryRemove(key, out _))
        {
            _logger.LogInformation("[RequestDedup] Explicitly cleared key {Key}", key);
        }
        else
        {
            _logger.LogDebug("[RequestDedup] Attempted to clear key {Key} but it was not found", key);
        }
    }
}
