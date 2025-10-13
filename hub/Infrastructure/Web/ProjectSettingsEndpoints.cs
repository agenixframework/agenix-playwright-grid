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

using System.Text.Json;
using Agenix.PlaywrightGrid.Shared.Logging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PlaywrightHub.Infrastructure.Web;

public static class ProjectSettingsEndpoints
{
    // Helper: Normalize retention values to always have a "d" suffix (e.g., "30" → "30d", "30d" → "30d")
    private static string NormalizeRetentionValue(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var trimmed = value.Trim();

        // Already has suffix
        if (trimmed.EndsWith('d') || trimmed.EndsWith('h'))
        {
            return trimmed;
        }

        // Numeric value without suffix - add "d"
        if (int.TryParse(trimmed, out _))
        {
            return $"{trimmed}d";
        }

        // Invalid format - return default
        return defaultValue;
    }

    private static readonly string[] Handler = ["1h", "3h", "6h", "12h", "1d", "3d", "7d"];
    private static readonly string[] HandlerArray = ["7", "14", "30", "90", "180", "7d", "14d", "30d", "90d", "180d"];

    public static void MapProjectSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/projects");

        // GET /api/projects/{projectKey}/settings - Get project settings
        group.MapGet("/{projectKey}/settings", async (HttpContext context, string projectKey, IDatabase db, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ProjectSettings");
            var chunkedLogger = new ChunkedLogger(logger, "ProjectSettings.Get");

            chunkedLogger.LogMilestone(
                EventCodes.ProjectSettings.SettingsRetrieved,
                "projectKey={ProjectKey} operation=Get",
                projectKey);

            try
            {
                var settingsKey = $"project:{projectKey}:settings";
                var json = await db.StringGetAsync(settingsKey);

                if (json.IsNullOrEmpty)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.ProjectSettings.SettingsMissing,
                        "projectKey={ProjectKey} returningDefaults=true",
                        projectKey);

                    return Results.Ok(new
                    {
                        launchInactivityTimeout = "1d",
                        keepLaunches = "30d",
                        keepLogs = "7d",
                        keepAttachments = "7d"
                    });
                }

                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json.ToString());
                var launchInactivityTimeout = "1d";
                var keepLaunches = "30d";
                var keepLogs = "7d";
                var keepAttachments = "7d";

                if (settings != null)
                {
                    if (settings.TryGetValue("launchInactivityTimeout", out var t) &&
                        t.ValueKind == JsonValueKind.String)
                    {
                        launchInactivityTimeout = NormalizeRetentionValue(t.GetString(), "1d");
                    }

                    if (settings.TryGetValue("keepLaunches", out var l) && l.ValueKind == JsonValueKind.String)
                    {
                        keepLaunches = NormalizeRetentionValue(l.GetString(), "30d");
                    }

                    if (settings.TryGetValue("keepLogs", out var log) && log.ValueKind == JsonValueKind.String)
                    {
                        keepLogs = NormalizeRetentionValue(log.GetString(), "7d");
                    }

                    if (settings.TryGetValue("keepAttachments", out var att) && att.ValueKind == JsonValueKind.String)
                    {
                        keepAttachments = NormalizeRetentionValue(att.GetString(), "7d");
                    }
                }

                chunkedLogger.LogMilestone(
                    EventCodes.ProjectSettings.SettingsRetrieved,
                    "projectKey={ProjectKey} settingsExist=true",
                    projectKey);

                return Results.Ok(new { launchInactivityTimeout, keepLaunches, keepLogs, keepAttachments });
            }
            catch (Exception ex)
            {
                chunkedLogger.LogMilestone(
                    EventCodes.ProjectSettings.SettingsRetrieved,
                    ex,
                    "error={Error} projectKey={ProjectKey}",
                    ex.Message, projectKey);

                return ProblemDetailsHelpers.InternalServerError(
                    "Failed to load project settings",
                    eventCode: EventCodes.Database.OperationFailed,
                    instance: context.Request.Path,
                    traceId: context.TraceIdentifier);
            }
        });

        // POST /api/projects/{projectKey}/settings - Save project settings
        group.MapPost("/{projectKey}/settings",
            async (HttpContext context, string projectKey, IDatabase db, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("ProjectSettings");
                var chunkedLogger = new ChunkedLogger(logger, "ProjectSettings.Update");

                chunkedLogger.LogMilestone(
                    EventCodes.ProjectSettings.SettingsValidationStarted,
                    "projectKey={ProjectKey} operation=Update",
                    projectKey);

                try
                {
                    var doc = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();

                    if (doc == null)
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.ProjectSettings.SettingsValidationFailed,
                            "projectKey={ProjectKey} reason=InvalidRequestBody",
                            projectKey);

                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["Request"] = ["Invalid request body"]
                            },
                            eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                            instance: context.Request.Path,
                            traceId: context.TraceIdentifier);
                    }

                    string? launchInactivityTimeout = null;
                    string? keepLaunches = null;
                    string? keepLogs = null;
                    string? keepAttachments = null;

                    if (doc.TryGetValue("launchInactivityTimeout", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        launchInactivityTimeout = NormalizeRetentionValue(t.GetString(), null!);
                    }

                    if (doc.TryGetValue("keepLaunches", out var l) && l.ValueKind == JsonValueKind.String)
                    {
                        keepLaunches = NormalizeRetentionValue(l.GetString(), null!);
                    }

                    if (doc.TryGetValue("keepLogs", out var log) && log.ValueKind == JsonValueKind.String)
                    {
                        keepLogs = NormalizeRetentionValue(log.GetString(), null!);
                    }

                    if (doc.TryGetValue("keepAttachments", out var att) && att.ValueKind == JsonValueKind.String)
                    {
                        keepAttachments = NormalizeRetentionValue(att.GetString(), null!);
                    }

                    var validTimeouts = Handler;
                    var validKeep = HandlerArray;

                    // Validate individual values (after normalization)
                    if (launchInactivityTimeout != null && !validTimeouts.Contains(launchInactivityTimeout))
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.ProjectSettings.RetentionValueInvalid,
                            "projectKey={ProjectKey} field=launchInactivityTimeout value={Value}",
                            projectKey, launchInactivityTimeout);

                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["launchInactivityTimeout"] = ["Invalid value. Allowed: 1h, 3h, 6h, 12h, 1d, 3d, 7d"]
                            },
                            eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                            instance: context.Request.Path,
                            traceId: context.TraceIdentifier);
                    }

                    if (keepLaunches != null && !validKeep.Contains(keepLaunches))
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.ProjectSettings.RetentionValueInvalid,
                            "projectKey={ProjectKey} field=keepLaunches value={Value}",
                            projectKey, keepLaunches);

                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["keepLaunches"] = ["Invalid value. Allowed: 7d, 14d, 30d, 90d, 180d (or without 'd' suffix)"]
                            },
                            eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                            instance: context.Request.Path,
                            traceId: context.TraceIdentifier);
                    }

                    if (keepLogs != null && !validKeep.Contains(keepLogs))
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.ProjectSettings.RetentionValueInvalid,
                            "projectKey={ProjectKey} field=keepLogs value={Value}",
                            projectKey, keepLogs);

                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["keepLogs"] = ["Invalid value. Allowed: 7d, 14d, 30d, 90d, 180d (or without 'd' suffix)"]
                            },
                            eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                            instance: context.Request.Path,
                            traceId: context.TraceIdentifier);
                    }

                    if (keepAttachments != null && !validKeep.Contains(keepAttachments))
                    {
                        chunkedLogger.LogMilestone(
                            EventCodes.ProjectSettings.RetentionValueInvalid,
                            "projectKey={ProjectKey} field=keepAttachments value={Value}",
                            projectKey, keepAttachments);

                        return ProblemDetailsHelpers.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["keepAttachments"] = ["Invalid value. Allowed: 7d, 14d, 30d, 90d, 180d (or without 'd' suffix)"]
                            },
                            eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                            instance: context.Request.Path,
                            traceId: context.TraceIdentifier);
                    }

                    // Validate retention hierarchy: Attachments <= Logs <= Launches
                    if (keepLaunches != null && keepLogs != null)
                    {
                        var launchesDays = int.Parse(keepLaunches.TrimEnd('d'));
                        var logsDays = int.Parse(keepLogs.TrimEnd('d'));
                        if (logsDays > launchesDays)
                        {
                            chunkedLogger.LogMilestone(
                                EventCodes.ProjectSettings.RetentionHierarchyViolation,
                                "projectKey={ProjectKey} violation=LogsGreaterThanLaunches logs={Logs} launches={Launches}",
                                projectKey, keepLogs, keepLaunches);

                            return ProblemDetailsHelpers.ValidationProblem(
                                new Dictionary<string, string[]>
                                {
                                    ["keepLogs"] = ["Validation failed: Logs retention must be ≤ Launches retention"]
                                },
                                eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                                instance: context.Request.Path,
                                traceId: context.TraceIdentifier);
                        }
                    }

                    if (keepLogs != null && keepAttachments != null)
                    {
                        var logsDays = int.Parse(keepLogs.TrimEnd('d'));
                        var attachmentsDays = int.Parse(keepAttachments.TrimEnd('d'));
                        if (attachmentsDays > logsDays)
                        {
                            chunkedLogger.LogMilestone(
                                EventCodes.ProjectSettings.RetentionHierarchyViolation,
                                "projectKey={ProjectKey} violation=AttachmentsGreaterThanLogs attachments={Attachments} logs={Logs}",
                                projectKey, keepAttachments, keepLogs);

                            return ProblemDetailsHelpers.ValidationProblem(
                                new Dictionary<string, string[]>
                                {
                                    ["keepAttachments"] = ["Validation failed: Attachments retention must be ≤ Logs retention"]
                                },
                                eventCode: EventCodes.ProjectSettings.RetentionValueInvalid,
                                instance: context.Request.Path,
                                traceId: context.TraceIdentifier);
                        }
                    }

                    // Get existing settings to merge
                    var settingsKey = $"project:{projectKey}:settings";
                    var existingJson = await db.StringGetAsync(settingsKey);

                    var existing = new { timeout = "1d", launches = "30d", logs = "7d", attachments = "7d" };

                    if (!existingJson.IsNullOrEmpty)
                    {
                        try
                        {
                            var exDoc =
                                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson.ToString());
                            if (exDoc != null)
                            {
                                if (exDoc.TryGetValue("launchInactivityTimeout", out var et))
                                {
                                    existing = existing with { timeout = et.GetString() ?? "1d" };
                                }

                                if (exDoc.TryGetValue("keepLaunches", out var el))
                                {
                                    existing = existing with { launches = el.GetString() ?? "30d" };
                                }

                                if (exDoc.TryGetValue("keepLogs", out var elog))
                                {
                                    existing = existing with { logs = elog.GetString() ?? "7d" };
                                }

                                if (exDoc.TryGetValue("keepAttachments", out var eatt))
                                {
                                    existing = existing with { attachments = eatt.GetString() ?? "7d" };
                                }
                            }
                        }
                        catch { }
                    }

                    chunkedLogger.LogMilestone(
                        EventCodes.ProjectSettings.SettingsValidationSucceeded,
                        "projectKey={ProjectKey} valuesValidated=true",
                        projectKey);

                    var final = new
                    {
                        launchInactivityTimeout = launchInactivityTimeout ?? existing.timeout,
                        keepLaunches = keepLaunches ?? existing.launches,
                        keepLogs = keepLogs ?? existing.logs,
                        keepAttachments = keepAttachments ?? existing.attachments
                    };

                    await db.StringSetAsync(settingsKey, JsonSerializer.Serialize(final));

                    chunkedLogger.LogMilestone(
                        EventCodes.ProjectSettings.SettingsPersisted,
                        "projectKey={ProjectKey} launchTimeout={LaunchTimeout} keepLaunches={KeepLaunches} keepLogs={KeepLogs} keepAttachments={KeepAttachments}",
                        projectKey, final.launchInactivityTimeout, final.keepLaunches, final.keepLogs, final.keepAttachments);

                    return Results.Ok(new
                    {
                        ok = true,
                        final.launchInactivityTimeout,
                        final.keepLaunches,
                        final.keepLogs,
                        final.keepAttachments
                    });
                }
                catch (Exception ex)
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.ProjectSettings.SettingsPersistenceFailed,
                        ex,
                        "error={Error} projectKey={ProjectKey}",
                        ex.Message, projectKey);

                    return ProblemDetailsHelpers.ServiceUnavailable(
                        dependency: "Redis",
                        safeMessage: "Failed to save project settings. Please try again later.",
                        eventCode: EventCodes.Redis.OperationFailed,
                        instance: context.Request.Path,
                        traceId: context.TraceIdentifier);
                }
            });
    }
}
