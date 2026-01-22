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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dashboard.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Dashboard.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RequestDeduplicationService"/> to validate correct behavior
/// under concurrency, failure scenarios, and lifecycle cleanup.
/// </summary>
public class RequestDeduplicationServiceTests
{
    private readonly ILogger<RequestDeduplicationService> _logger;
    private readonly RequestDeduplicationService _service;

    public RequestDeduplicationServiceTests()
    {
        _logger = Substitute.For<ILogger<RequestDeduplicationService>>();
        _service = new RequestDeduplicationService(_logger);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCallsSameKey_ExecutesOnlyOnce()
    {
        // Arrange
        const string key = "GET:/api/test";
        int executionCount = 0;
        const int concurrentRequests = 15;

        // Act
        var tasks = Enumerable.Range(0, concurrentRequests).Select(_ =>
            _service.ExecuteAsync(key, async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(100); // Simulate long-running operation
                return "Result";
            })
        ).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        executionCount.Should().Be(1);
        results.Should().AllBeEquivalentTo("Result");
        results.Length.Should().Be(concurrentRequests);
    }

    [Fact]
    public async Task ExecuteAsync_DifferentKeys_ExecuteIndependently()
    {
        // Arrange
        int executionCount = 0;
        const int numKeys = 5;

        // Act
        var tasks = Enumerable.Range(0, numKeys).Select(i =>
            _service.ExecuteAsync($"key:{i}", async () =>
            {
                Interlocked.Increment(ref executionCount);
                await Task.Delay(50);
                return $"Result:{i}";
            })
        ).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        executionCount.Should().Be(numKeys);
        for (int i = 0; i < numKeys; i++)
        {
            results.Should().Contain($"Result:{i}");
        }
    }

    [Fact]
    public async Task ExecuteAsync_OperationFails_KeyIsRemovedAndCanRetry()
    {
        // Arrange
        const string key = "GET:/api/fail";
        int executionCount = 0;

        // Act & Assert - First call fails
        var firstCall = _service.ExecuteAsync<string>(key, async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(50);
            throw new InvalidOperationException("Failed");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => firstCall);
        executionCount.Should().Be(1);

        // Second call should succeed
        var secondCallResult = await _service.ExecuteAsync(key, async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(50);
            return "Success";
        });

        secondCallResult.Should().Be("Success");
        executionCount.Should().Be(2);
    }

    [Fact]
    public async Task Clear_ExistingKey_RemovesFromCache()
    {
        // Arrange
        const string key = "GET:/api/clear";
        int executionCount = 0;

        // Start an operation that hasn't completed yet
        var tcs = new TaskCompletionSource<string>();
        var firstTask = _service.ExecuteAsync(key, () =>
        {
            Interlocked.Increment(ref executionCount);
            return tcs.Task;
        });

        executionCount.Should().Be(1);

        // Act
        _service.Clear(key);

        // Start another operation with same key
        var secondTask = _service.ExecuteAsync(key, async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(10);
            return "Second";
        });

        // Assert
        executionCount.Should().Be(2);

        tcs.SetResult("First");
        var firstResult = await firstTask;
        var secondResult = await secondTask;

        firstResult.Should().Be("First");
        secondResult.Should().Be("Second");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulCompletion_RemovesFromCache()
    {
        // Arrange
        const string key = "GET:/api/complete";
        int executionCount = 0;

        // Act
        var result1 = await _service.ExecuteAsync(key, async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(10);
            return "Result1";
        });

        executionCount.Should().Be(1);

        var result2 = await _service.ExecuteAsync(key, async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(10);
            return "Result2";
        });

        // Assert
        executionCount.Should().Be(2);
        result1.Should().Be("Result1");
        result2.Should().Be("Result2");
    }

    [Fact]
    public void Clear_NonExistentKey_DoesNotThrow()
    {
        // Act
        Action act = () => _service.Clear("non-existent");

        // Assert
        act.Should().NotThrow();
    }
}
