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

using System.Collections.Generic;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace PlaywrightHub.Infrastructure.Web;

public static class TestErrorEndpoints
{
    public static void MapTestErrorEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/test/error");

        group.MapGet("/validation", () =>
        {
            var errors = new Dictionary<string, string[]>
            {
                ["FieldA"] = new[] { "Error 1" }
            };
            return ProblemDetailsHelpers.ValidationProblem(errors, "ADM91");
        });

        group.MapGet("/bad-request", (HttpContext httpContext) =>
        {
            return ProblemDetailsHelpers.ValidationProblem(
                new Dictionary<string, string[]> { ["test"] = ["Manual bad request"] },
                eventCode: "TEST01",
                instance: httpContext.Request.Path,
                traceId: httpContext.TraceIdentifier);
        });

        group.MapGet("/unhandled", () =>
        {
            throw new System.Exception("Unhandled test exception");
        });

        group.MapGet("/database", () =>
        {
            // Simulate a unique constraint violation (23505)
            throw new PostgresException("Duplicate key violation", "ERROR", "ERROR", "23505");
        });

        // Special routes to test context-aware timeout mapping
        routes.MapGet("/api/launches/test-error-timeout", () =>
        {
            throw new System.TimeoutException("Launch operation timed out");
        });

        routes.MapGet("/api/test-items/test-error-timeout", () =>
        {
            throw new System.TimeoutException("Test item operation timed out");
        });
    }
}
