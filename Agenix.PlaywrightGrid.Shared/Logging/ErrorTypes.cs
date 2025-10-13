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

namespace Agenix.PlaywrightGrid.Shared.Logging;

/// <summary>
///     Classification of error types for structured error logging.
/// </summary>
public enum ErrorType
{
    /// <summary>
    ///     Input validation failure (bad request, invalid parameters).
    /// </summary>
    Validation,

    /// <summary>
    ///     Requested resource not found (404).
    /// </summary>
    NotFound,

    /// <summary>
    ///     Conflict with existing state (409, duplicate key, etc.).
    /// </summary>
    Conflict,

    /// <summary>
    ///     Operation timeout (database query, HTTP request, browser operation).
    /// </summary>
    Timeout,

    /// <summary>
    ///     External dependency failure (database, Redis, RabbitMQ, MinIO, worker node).
    /// </summary>
    DependencyFailure,

    /// <summary>
    ///     Authorization/authentication failure (401, 403).
    /// </summary>
    Unauthorized,

    /// <summary>
    ///     Capacity/resource exhaustion (connection pool, memory, disk space).
    /// </summary>
    ResourceExhaustion,

    /// <summary>
    ///     Unexpected error (unhandled exception, programming error).
    /// </summary>
    Unexpected
}

/// <summary>
///     External dependency names for dependency failure tracking.
/// </summary>
public enum DependencyName
{
    /// <summary>
    ///     PostgresSQL database.
    /// </summary>
    Database,

    /// <summary>
    ///     Redis cache/queue.
    /// </summary>
    Redis,

    /// <summary>
    ///     RabbitMQ message broker.
    /// </summary>
    RabbitMQ,

    /// <summary>
    ///     MinIO/S3 object storage.
    /// </summary>
    MinIO,

    /// <summary>
    ///     Worker node (browser pool service).
    /// </summary>
    Worker,

    /// <summary>
    ///     Hub service (API).
    /// </summary>
    Hub,

    /// <summary>
    ///     Ingestion service.
    /// </summary>
    Ingestion,

    /// <summary>
    ///     Playwright browser automation library.
    /// </summary>
    Playwright,

    /// <summary>
    ///     External HTTP API.
    /// </summary>
    ExternalApi,

    /// <summary>
    ///     File system.
    /// </summary>
    FileSystem,

    /// <summary>
    ///     Results store (Postgres/Redis).
    /// </summary>
    ResultsStore
}
