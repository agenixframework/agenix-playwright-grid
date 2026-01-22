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

namespace Dashboard.Models;

/// <summary>
/// Represents comprehensive details about an HTTP error.
/// </summary>
public record ErrorDetails
{
    /// <summary>
    /// Gets the user-friendly error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the descriptive title for the error.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets additional technical details about the error.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets the exception stack trace if available.
    /// </summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Gets the unique request identifier.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets the specific event code associated with the error.
    /// </summary>
    public string? EventCode { get; init; }

    /// <summary>
    /// Gets the HTTP method used for the request.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the API endpoint that was called.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the request can be retried.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Gets the category of the error.
    /// </summary>
    public ErrorCategory Category { get; init; }
}
