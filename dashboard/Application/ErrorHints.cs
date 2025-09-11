#region License
// Copyright (c) 2025 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
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

using Microsoft.Extensions.Configuration;

namespace Dashboard.Application;

internal static class ErrorHints
{
    public static string ForHttpFailure(int statusCode, string? reason)
    {
        return statusCode switch
        {
            401 => "Unauthorized – likely HUB_RUNNER_SECRET mismatch. Set dashboard env HUB_RUNNER_SECRET to the same value as the hub and restart both.",
            404 => "Not found – check HUB_SIGNALR/HUB base URL and that the hub is running (GET /health should be 200).",
            429 => "Rate limited – hub is protecting itself. Reduce request rate or increase capacity; try again after the Retry-After period.",
            503 => "Service unavailable – capacity may be missing. Start worker(s) with POOL_CONFIG and ensure NODE_SECRET matches the hub’s HUB_NODE_SECRET.",
            _ => reason ?? "Request failed. See hub logs for details."
        };
    }

    public static string ForWebSocket(string? lastError, IConfiguration config)
    {
        var hubSignalR = config["HUB_SIGNALR"] ?? "http://hub:5000/ws";
        var baseMsg = "WebSocket disconnected from hub.";
        var specifics = string.IsNullOrWhiteSpace(lastError) ? string.Empty : $" ({lastError})";
        var tip = $" Tip: verify HUB_SIGNALR is reachable: {hubSignalR}. If running locally use 'docker compose up --build' and open http://127.0.0.1:5100/health. Check reverse proxy/CORS/network.";
        return baseMsg + specifics + tip;
    }
}
