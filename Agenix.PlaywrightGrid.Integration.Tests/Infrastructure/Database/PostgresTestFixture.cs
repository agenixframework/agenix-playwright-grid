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

using Npgsql;

namespace Agenix.PlaywrightGrid.Integration.Tests.Infrastructure.Database;

/// <summary>
///     Singleton fixture for PostgreSQL database connections in integration tests.
///     Provides a shared NpgsqlDataSource for all database tests to improve performance
///     and reduce connection overhead.
/// </summary>
public sealed class PostgresTestFixture : IDisposable
{
    private static readonly Lazy<PostgresTestFixture> LazyInstance = new(() => new PostgresTestFixture());
    private readonly NpgsqlDataSource _dataSource;
    private bool _disposed;

    private PostgresTestFixture()
    {
        ConnectionString = BuildConnectionString();
        _dataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    /// <summary>
    ///     Gets the singleton instance of the PostgresTestFixture.
    /// </summary>
    public static PostgresTestFixture Instance => LazyInstance.Value;

    /// <summary>
    ///     Gets the connection string used for database connections.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    ///     Gets the shared NpgsqlDataSource for executing database commands.
    /// </summary>
    public NpgsqlDataSource DataSource
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _dataSource;
        }
    }

    /// <summary>
    ///     Disposes the shared data source. This should only be called at the end of all tests.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _dataSource.Dispose();
        _disposed = true;
    }

    /// <summary>
    ///     Builds the PostgreSQL connection string from environment variables.
    ///     Falls back to default localhost values if not set.
    ///
    ///     Supports both Docker and local mode via GRID_TESTS_USE_LOCAL flag:
    ///     - Docker mode (default): Connects to Docker container on localhost:5432
    ///     - Local mode: Connects to local PostgreSQL on localhost:5432
    ///     Both modes use the same connection string (localhost:5432).
    /// </summary>
    private static string BuildConnectionString()
    {
        // Log which mode we're using
        var useLocal = TestConfiguration.UseLocalServices;
        Console.WriteLine($"[PostgresTestFixture] {TestConfiguration.EnvironmentDescription}");
        Console.WriteLine($"[PostgresTestFixture] Connecting to PostgreSQL at {TestConfiguration.PostgresHost}:{TestConfiguration.PostgresPort}");

        // First check if full connection string is provided
        var postgresConnectionString = Environment.GetEnvironmentVariable("HUB_RESULTS_POSTGRES");

        if (!string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            return postgresConnectionString;
        }

        // Build connection string from TestConfiguration centralized configuration
        var host = TestConfiguration.PostgresHost;
        var port = TestConfiguration.PostgresPort;
        var user = TestConfiguration.PostgresUser;
        var password = TestConfiguration.PostgresPassword;
        var database = TestConfiguration.PostgresDatabase;

        return $"Host={host};Port={port};Username={user};Password={password};Database={database};" +
               "Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;" +
               "Connection Idle Lifetime=300;Connection Pruning Interval=10;" +
               "Max Auto Prepare=20;Auto Prepare Min Usages=1";
    }
}
