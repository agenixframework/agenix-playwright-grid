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

using System.Globalization;
using System.Text.Json;
using HousekeepingService.Shared;
using StackExchange.Redis;

namespace HousekeepingService.Infrastructure;

public class ProjectSettingsReader(
    IDatabase db,
    IConnectionMultiplexer mux,
    ILogger<ProjectSettingsReader> logger) : IProjectSettingsReader
{
    public async Task<List<string>> GetAllProjectKeysAsync()
    {
        var projects = new List<string>();
        try
        {
            var server = mux.GetServer(mux.GetEndPoints()[0]);
            await foreach (var key in server.KeysAsync(pattern: "project:*:settings", pageSize: 100))
            {
                var parts = key.ToString().Split(':');
                if (parts is ["project", _, "settings", ..])
                {
                    projects.Add(parts[1]);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get project keys from Redis");
        }

        return projects.Distinct().ToList();
    }

    public async Task<RetentionSettings?> GetRetentionSettingsAsync(string projectKey)
    {
        try
        {
            var json = await db.StringGetAsync($"project:{projectKey}:settings");
            if (json.IsNullOrEmpty)
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json.ToString());
            if (settings == null)
            {
                return null;
            }

            return new RetentionSettings
            {
                ProjectKey = projectKey,
                KeepLaunchesDays = TryGetInt(settings, "keepLaunches", 30),
                KeepLogsDays = TryGetInt(settings, "keepLogs", 7),
                KeepAttachmentsDays = TryGetInt(settings, "keepAttachments", 7),
                KeepAuditDays = TryGetInt(settings, "keepAudit", 90),
                LaunchInactivityTimeout = TryGetString(settings, "launchInactivityTimeout")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get retention settings for {ProjectKey}", projectKey);
            return null;
        }
    }

    public async Task<RetentionSettings?> GetAsync(string projectKey, CancellationToken ct = default)
    {
        return await GetRetentionSettingsAsync(projectKey);
    }

    private static int TryGetInt(Dictionary<string, JsonElement> dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(key, out var element))
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var str = element.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    // Strip "d" suffix if present (handles "30d" and "30")
                    str = str.TrimEnd('d').Trim();

                    // Try parsing as double (handles fractions like "0.003472" for testing)
                    // Use InvariantCulture to ensure decimal point is recognized regardless of system locale
                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var daysDouble))
                    {
                        // Floor to integer (0.003472 days → 0 days)
                        return (int)Math.Floor(daysDouble);
                    }
                }
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                // Handle JSON numbers (could be int or double)
                try { return (int)Math.Floor(element.GetDouble()); }
                catch { return element.GetInt32(); }
            }
        }

        return defaultValue;
    }

    private static string? TryGetString(Dictionary<string, JsonElement> dict, string key)
    {
        if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }
}
