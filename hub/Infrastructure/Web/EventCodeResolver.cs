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

using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using PlaywrightHub.Application.Ports;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

/// <summary>
/// Implementation of IEventCodeResolver that maps exceptions and status codes to EventCodes.
/// </summary>
public class EventCodeResolver : IEventCodeResolver
{
    private readonly ILogger<EventCodeResolver> _logger;

    public EventCodeResolver(ILogger<EventCodeResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string ResolveEventCode(Exception exception, HttpContext context)
    {
        var eventCode = exception switch
        {
            NpgsqlException ex => MapDatabaseException(ex),
            RedisException => EventCodes.Redis.OperationFailed,
            TimeoutException => MapTimeoutException(context),
            InvalidOperationException => EventCodes.WebServer.RequestFailed,
            _ => EventCodes.WebServer.RequestFailed
        };

        _logger.LogDebug("Resolved {ExceptionType} to event code {EventCode}",
            exception.GetType().Name, eventCode);

        return eventCode;
    }

    /// <inheritdoc />
    public string ResolveEventCodeFromStatus(int statusCode, HttpContext context)
    {
        var eventCode = statusCode switch
        {
            400 => EventCodes.AdminProjectsUsers.Validation.ValidationFailed,
            401 => EventCodes.AdminProjectsUsers.Authentication.LoginFailed,
            500 or 503 => EventCodes.WebServer.RequestFailed,
            _ => EventCodes.WebServer.RequestFailed
        };

        _logger.LogDebug("Resolved status code {StatusCode} to event code {EventCode}",
            statusCode, eventCode);

        return eventCode;
    }

    private string MapDatabaseException(NpgsqlException ex)
    {
        // Map PostgreSQL error codes to event codes
        return ex.SqlState switch
        {
            "23505" => EventCodes.Database.TransactionFailed, // Unique violation
            "23503" => EventCodes.Database.TransactionFailed, // Foreign key violation
            "40001" => EventCodes.Database.TransactionFailed, // Serialization failure
            _ => EventCodes.Database.OperationFailed
        };
    }

    private string MapTimeoutException(HttpContext context)
    {
        // Context-aware timeout mapping based on request path
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.Contains("/api/launches", StringComparison.OrdinalIgnoreCase))
        {
            return EventCodes.Launch.LaunchOperationFailed;
        }

        if (path.Contains("/api/test-items", StringComparison.OrdinalIgnoreCase))
        {
            return EventCodes.TestItem.TestItemOperationFailed;
        }

        return EventCodes.WebServer.RequestFailed;
    }
}
