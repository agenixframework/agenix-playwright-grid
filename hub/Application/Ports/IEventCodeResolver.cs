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

using Microsoft.AspNetCore.Http;

namespace PlaywrightHub.Application.Ports;

/// <summary>
/// Resolves exception and error scenarios to EventCodes for consistent error identification.
/// </summary>
public interface IEventCodeResolver
{
    /// <summary>
    /// Resolves an exception to an appropriate EventCode based on exception type and HTTP context.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="context">The HTTP context (for endpoint-specific mapping).</param>
    /// <returns>Event code (e.g., "DB04", "LCH05", "WSH10").</returns>
    string ResolveEventCode(Exception exception, HttpContext context);

    /// <summary>
    /// Resolves an HTTP status code to a generic EventCode (fallback when no exception available).
    /// </summary>
    /// <param name="statusCode">HTTP status code (400, 404, 500, etc.).</param>
    /// <param name="context">The HTTP context.</param>
    /// <returns>Generic event code (e.g., "ADM91" for 400, "WSH10" for 500).</returns>
    string ResolveEventCodeFromStatus(int statusCode, HttpContext context);
}
