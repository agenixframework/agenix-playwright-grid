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

using StackExchange.Redis;

namespace Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Redis;

/// <summary>
///     Singleton fixture for Redis connections in integration tests.
///     Provides a shared ConnectionMultiplexer for all Redis operations to improve performance
///     and reduce connection overhead.
/// </summary>
public sealed class RedisTestFixture : IDisposable
{
    private static readonly Lazy<RedisTestFixture> LazyInstance = new(() => new RedisTestFixture());
    private readonly ConnectionMultiplexer _multiplexer;
    private bool _disposed;

    private RedisTestFixture()
    {
        ConnectionString = GetConnectionString();
        _multiplexer = ConnectionMultiplexer.Connect(ConnectionString);
    }

    /// <summary>
    ///     Gets the singleton instance of the RedisTestFixture.
    /// </summary>
    public static RedisTestFixture Instance => LazyInstance.Value;

    /// <summary>
    ///     Gets the Redis connection string used for connections.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    ///     Gets the shared ConnectionMultiplexer for Redis operations.
    /// </summary>
    public ConnectionMultiplexer Multiplexer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _multiplexer;
        }
    }

    /// <summary>
    ///     Disposes the shared multiplexer. This should only be called at the end of all tests.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _multiplexer.Dispose();
        _disposed = true;
    }

    /// <summary>
    ///     Gets a database instance for Redis operations.
    /// </summary>
    /// <param name="db">The database number (default is 0).</param>
    /// <returns>An IDatabase instance for the specified database.</returns>
    public IDatabase GetDatabase(int db = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _multiplexer.GetDatabase(db);
    }

    /// <summary>
    ///     Gets the Redis connection string from environment variables.
    ///     Falls back to localhost:6379 if not set.
    ///
    ///     Supports both Docker and local mode via GRID_TESTS_USE_LOCAL flag:
    ///     - Docker mode (default): Connects to Docker container on localhost:6379
    ///     - Local mode: Connects to local Redis on localhost:6379
    ///     Both modes use the same connection string (localhost:6379).
    /// </summary>
    private static string GetConnectionString()
    {
        // Log which mode we're using
        Console.WriteLine($"[RedisTestFixture] {TestConfiguration.EnvironmentDescription}");
        Console.WriteLine($"[RedisTestFixture] Connecting to Redis at {TestConfiguration.RedisConnection}");

        return TestConfiguration.RedisConnection;
    }
}
