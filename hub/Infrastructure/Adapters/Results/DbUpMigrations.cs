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

using System.Reflection;
using Agenix.PlaywrightGrid.Shared.Logging;
using DbUp;
using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog;
using Serilog.Extensions.Logging;

namespace PlaywrightHub.Infrastructure.Adapters.Results;

/// <summary>
///     Professional database migration management using DbUp.
///     DbUp is an industry-standard .NET migration tool that handles versioning,
///     tracking, and migration execution with excellent logging and error handling.
///
///     This class uses Serilog's static Log for structured logging with event codes
///     since migrations run before DI is configured.
/// </summary>
internal static class DbUpMigrations
{
    /// <summary>
    ///     Applies all pending migrations using DbUp with comprehensive instrumentation.
    ///     Uses ChunkedLogger with MigrationOperationScope for structured operation tracking.
    /// </summary>
    internal static async Task ApplyAsync(string connectionString, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            // IMPORTANT: Get logger from static provider (works before DI configured)
            // Use SerilogLoggerFactory directly to avoid redundant sink instances
            var logger = new SerilogLoggerFactory(Log.Logger).CreateLogger("DbUpMigrations");
            var chunkedLogger = new ChunkedLogger(logger, "DbUpMigrations");

            // Create operation scope for migration tracking
            using var operation = ChunkedLogger.BeginMigrationOperation(
                logger,
                "DbUpMigration:Apply",
                new Dictionary<string, object>
                {
                    ["Connection"] = HideSensitiveInfo(connectionString)
                });

            try
            {
                // Milestone: Migration started
                chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationStarted,
                    "Database migration started connection={Connection}",
                    HideSensitiveInfo(connectionString));

                // Milestone: Testing database connection
                chunkedLogger.LogMilestone(
                    EventCodes.Database.DatabaseConnectionTested,
                    "Testing database connection");

                using (var testConn = new NpgsqlConnection(connectionString))
                {
                    testConn.Open();
                    chunkedLogger.LogMilestone(
                        EventCodes.Database.DatabaseReady,
                        "Database connection successful");
                }

                // Create DbUp upgrader with PostgresSQL provider and chunked logging
                var upgrader = DeployChanges.To
                    .PostgresqlDatabase(connectionString)
                    .WithScriptsEmbeddedInAssembly(
                        Assembly.GetExecutingAssembly(),
                        script => script.Contains(".Migrations."))
                    .WithTransaction()
                    .LogTo(new ChunkedUpgradeLog(logger))
                    .Build();

                // Check if upgrade is required (DbUp will log details via ChunkedUpgradeLog)
                if (!upgrader.IsUpgradeRequired())
                {
                    chunkedLogger.LogMilestone(
                        EventCodes.Database.MigrationUpgradeNotRequired,
                        "Database is up to date, no migrations needed");
                    return;
                }

                // Discover scripts count
                var scriptsToExecute = upgrader.GetScriptsToExecute();
                chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationScriptsDiscovered,
                    "Discovered {ScriptCount} migration script(s) to execute",
                    scriptsToExecute.Count());

                // Perform the upgrade (DbUp will log transaction start/commit, script execution via ChunkedUpgradeLog)
                var result = upgrader.PerformUpgrade();

                if (!result.Successful)
                {
                    logger.LogError(result.Error,
                        "{EventCode} Migration failed error={Error}",
                        EventCodes.Database.MigrationFailed,
                        result.Error.Message);

                    throw new InvalidOperationException("Database migration failed. See logs for details.",
                        result.Error);
                }

                // Final verification and completion
                chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationCompletedSuccessfully,
                    "Database migration completed successfully, applied {ScriptCount} script(s)",
                    result.Scripts?.Count() ?? 0);
            }
            catch (NpgsqlException ex)
            {
                logger.LogError(ex,
                    "{EventCode} Database connection failed error={Error}",
                    EventCodes.Database.MigrationConnectionFailed,
                    ex.Message);

                throw new InvalidOperationException("Database migration failed. Connection error. See logs for details.", ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "{EventCode} Migration failed unexpectedly error={Error}",
                    EventCodes.Database.MigrationFailed,
                    ex.Message);

                if (ex.InnerException != null)
                {
                    logger.LogError(ex.InnerException,
                        "Migration inner exception: {InnerError}",
                        ex.InnerException.Message);
                }

                throw new InvalidOperationException("Database migration failed. See logs for details.", ex);
            }
        }, ct);
    }

    /// <summary>
    ///     Hides sensitive information from a connection string for logging.
    /// </summary>
    private static string HideSensitiveInfo(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = "***";
        }

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Upgrade log that writes to ChunkedLogger with event codes and structured properties.
    ///     Uses ILogger from LoggerFactory.Create since migrations run before DI is configured.
    /// </summary>
    private sealed class ChunkedUpgradeLog : IUpgradeLog
    {
        private readonly ChunkedLogger _chunkedLogger;

        public ChunkedUpgradeLog(Microsoft.Extensions.Logging.ILogger logger)
        {
            _chunkedLogger = new ChunkedLogger(logger, "DbUpMigrations");
        }

        public void LogTrace(string format, params object[] args)
        {
            _chunkedLogger.LogDebug(null, format, args);
        }

        public void LogDebug(string format, params object[] args)
        {
            _chunkedLogger.LogDebug(null, format, args);
        }

        public void LogInformation(string format, params object[] args)
        {
            // Map to MIGRATION event codes when appropriate patterns detected
            var message = string.Format(format, args);

            if (message.Contains("Upgrade detected") || message.Contains("upgrade is required"))
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationUpgradeRequired,
                    format, args);
            }
            else if (message.Contains("Executing Database Server script"))
            {
                var scriptName = ExtractScriptName(message);
                _chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationScriptExecuting,
                    "Executing script: {ScriptName}",
                    scriptName);
            }
            else if (message.Contains("Beginning transaction"))
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationTransactionStarted,
                    format, args);
            }
            else if (message.Contains("Committing transaction"))
            {
                _chunkedLogger.LogMilestone(
                    EventCodes.Database.MigrationTransactionCommitted,
                    format, args);
            }
            else if (message.Contains("Checking whether journal table exists"))
            {
                _chunkedLogger.LogDebug(null, format, args);
            }
            else
            {
                _chunkedLogger.LogDebug(null, format, args);
            }
        }

        public void LogWarning(string format, params object[] args)
        {
            _chunkedLogger.LogWarning(null, format, args);
        }

        public void LogError(string format, params object[] args)
        {
            _chunkedLogger.LogMilestone(
                EventCodes.Database.MigrationFailed,
                format, args);
        }

        public void LogError(Exception ex, string format, params object[] args)
        {
            // ChunkedLogger.LogMilestone doesn't accept Exception, so format the message and log milestone
            var message = string.Format(format, args);
            _chunkedLogger.LogMilestone(
                EventCodes.Database.MigrationFailed,
                "{Message} Exception={ExceptionMessage}",
                message, ex.Message);
        }

        private static string ExtractScriptName(string message)
        {
            // Extract from "Executing Database Server script 'V1__init.sql'"
            var startIdx = message.IndexOf('\'');
            if (startIdx >= 0)
            {
                var endIdx = message.IndexOf('\'', startIdx + 1);
                if (endIdx > startIdx)
                {
                    return message.Substring(startIdx + 1, endIdx - startIdx - 1);
                }
            }
            return "unknown";
        }
    }
}
