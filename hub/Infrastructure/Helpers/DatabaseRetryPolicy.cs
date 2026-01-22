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
using Polly;
using Polly.Retry;

namespace PlaywrightHub.Infrastructure.Helpers;

/// <summary>
///     Retry policy for transient database errors using Polly.
///     Handles connection failures, timeouts, and deadlocks.
/// </summary>
public static class DatabaseRetryPolicy
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500)
    ];

    public static AsyncRetryPolicy CreateRetryPolicy(ILogger logger)
    {
        return Policy
            .Handle<NpgsqlException>(IsTransient)
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                RetryDelays,
                (exception, timeSpan, retryCount, context) =>
                {
                    logger.LogWarning(
                        exception,
                        "Database operation failed. Retry {RetryCount} after {Delay}ms",
                        retryCount,
                        timeSpan.TotalMilliseconds);
                });
    }

    private static bool IsTransient(NpgsqlException ex)
    {
        // Transient error codes that are safe to retry
        return ex.SqlState switch
        {
            "40001" => true, // serialization_failure
            "40P01" => true, // deadlock_detected
            "53300" => true, // too_many_connections
            "57P03" => true, // cannot_connect_now
            "58000" => true, // system_error
            "58030" => true, // io_error
            "08000" => true, // connection_exception
            "08003" => true, // connection_does_not_exist
            "08006" => true, // connection_failure
            _ => false
        };
    }
}
