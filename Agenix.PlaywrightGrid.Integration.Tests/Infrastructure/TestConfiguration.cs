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

namespace Agenix.PlaywrightGrid.Integration.Tests.Infrastructure;

/// <summary>
///     Centralized configuration for integration test environment.
///     Determines whether tests run against Docker containers or local services based on AGENIX_TESTS_USE_LOCAL flag.
/// </summary>
public static class TestConfiguration
{
    /// <summary>
    ///     Gets whether tests should run against local services (dotnet run) instead of Docker containers.
    ///     Set AGENIX_TESTS_USE_LOCAL=true in .env to enable local mode.
    /// </summary>
    public static bool UseLocalServices { get; } = GetBooleanEnvironmentVariable("AGENIX_TESTS_USE_LOCAL", false);

    /// <summary>
    ///     Gets the Hub URL for API calls.
    ///     Defaults to http://localhost:5100 (same for Docker and local mode).
    /// </summary>
    public static string HubUrl { get; } = Environment.GetEnvironmentVariable("HUB_URL") ?? "http://localhost:5100";

    /// <summary>
    ///     Gets the PostgreSQL host.
    ///     Defaults to localhost (same for Docker and local mode).
    /// </summary>
    public static string PostgresHost { get; } = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";

    /// <summary>
    ///     Gets the PostgreSQL port.
    ///     Defaults to 5432 (same for Docker and local mode).
    /// </summary>
    public static string PostgresPort { get; } = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";

    /// <summary>
    ///     Gets the PostgreSQL username.
    ///     Defaults to postgres.
    /// </summary>
    public static string PostgresUser { get; } = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";

    /// <summary>
    ///     Gets the PostgreSQL password.
    ///     Defaults to postgres.
    /// </summary>
    public static string PostgresPassword { get; } = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";

    /// <summary>
    ///     Gets the PostgreSQL database name.
    ///     Defaults to agenix_reportportal.
    /// </summary>
    public static string PostgresDatabase { get; } = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "agenix_reportportal";

    /// <summary>
    ///     Gets the Redis connection string.
    ///     Defaults to localhost:6379 (same for Docker and local mode).
    /// </summary>
    public static string RedisConnection { get; } = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";

    /// <summary>
    ///     Gets a descriptive message about the current test environment configuration.
    /// </summary>
    public static string EnvironmentDescription =>
        UseLocalServices
            ? "Running tests against LOCAL services (dotnet run)"
            : "Running tests against DOCKER containers";

    /// <summary>
    ///     Parses a boolean environment variable with a default value.
    /// </summary>
    private static bool GetBooleanEnvironmentVariable(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "1" => true,
            "yes" => true,
            "false" => false,
            "0" => false,
            "no" => false,
            _ => defaultValue
        };
    }
}
