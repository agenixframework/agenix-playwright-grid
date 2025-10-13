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
using PlaywrightHub.Application.DTOs;

namespace PlaywrightHub.Application.Ports;

/// <summary>
///     Abstraction for persisting and querying run summaries, test cases, and command logs
///     used by the Hub Results HTTP endpoints and the SignalR ResultsHub consumed by the Dashboard.
///     Implementations may be in-memory and ephemeral (development/testing) or durable (e.g., Redis/DB).
/// </summary>
public interface IResultsStore
{
    /// <summary>
    ///     Inserts or updates a run summary.
    /// </summary>
    /// <param name="run">Run summary to upsert.</param>
    Task UpsertRunAsync(ResultRunSummaryDto run);

    /// <summary>
    ///     Retrieves a run summary by its id.
    /// </summary>
    /// <param name="runId">The unique run identifier.</param>
    /// <returns>The run summary or null if not found.</returns>
    Task<ResultRunSummaryDto?> GetRunAsync(string runId);

    /// <summary>
    ///     Pages run summaries optionally filtered by status/app/browser/env.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take (page size).</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="app">Optional app filter.</param>
    /// <param name="browser">Optional browser filter.</param>
    /// <param name="env">Optional environment filter.</param>
    Task<IReadOnlyList<ResultRunSummaryDto>> GetRunsAsync(int skip = 0, int take = 100,
        string? status = null, string? app = null, string? browser = null, string? env = null);

    /// <summary>
    ///     Returns the total number of runs for the given optional filters.
    /// </summary>
    Task<int> GetRunsCountAsync(string? status = null, string? app = null, string? browser = null, string? env = null);

    /// <summary>
    ///     Appends a command/log event for a run.
    /// </summary>
    /// <param name="ev">The event to store.</param>
    Task AppendCommandAsync(CommandLogEventDto ev);

    /// <summary>
    ///     Pages command/log events for a specific run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    Task<IReadOnlyList<CommandLogEventDto>> GetCommandsAsync(string runId, int skip = 0, int take = 200);

    /// <summary>
    ///     Returns the total number of command/log events for a run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    Task<int> GetCommandCountAsync(string runId);

    /// <summary>
    ///     Inserts or updates a test case record.
    /// </summary>
    /// <param name="test">Test case to upsert.</param>
    Task UpsertTestAsync(ResultTestCaseDto test);

    /// <summary>
    ///     Pages test cases for a specific run with optional status filter.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="status">Optional test status filter.</param>
    Task<IReadOnlyList<ResultTestCaseDto>> GetTestsAsync(string runId, int skip = 0, int take = 200,
        string? status = null);

    /// <summary>
    ///     Deletes a run and all associated tests and command logs.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>True if a run existed and was deleted; otherwise false.</returns>
    Task<bool> DeleteRunAsync(string runId);

    // ===== Test Case Management =====

    /// <summary>
    ///     Inserts or updates a test case with all its details.
    /// </summary>
    /// <param name="testCase">Test case to upsert.</param>
    Task UpsertTestCaseAsync(TestCaseDetailDto testCase);

    /// <summary>
    ///     Retrieves a specific test case by run ID and test ID.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <param name="testId">Test identifier.</param>
    /// <returns>The test case or null if not found.</returns>
    Task<TestCaseDetailDto?> GetTestCaseAsync(string runId, string testId);

    /// <summary>
    ///     Retrieves all test cases for a specific run.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <returns>List of test cases belonging to the run.</returns>
    Task<List<TestCaseDetailDto>> GetTestCasesForRunAsync(string runId);

    /// <summary>
    ///     Deletes all test cases associated with a run.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    Task DeleteTestCasesForRunAsync(string runId);

    /// <summary>
    ///     Counts total test cases for a run.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <returns>Total count of test cases.</returns>
    Task<int> CountTestCasesForRunAsync(string runId);

    // ===== Test Result Aggregation Methods (Phase 2) =====

    /// <summary>
    ///     Updates test result aggregations and computed status for a test run.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <param name="totalTests">Total number of tests.</param>
    /// <param name="passedTests">Number of passed tests.</param>
    /// <param name="failedTests">Number of failed tests.</param>
    /// <param name="skippedTests">Number of skipped tests.</param>
    /// <param name="timedoutTests">Number of timed out tests.</param>
    /// <param name="computedStatus">Computed status string.</param>
    Task UpdateTestRunAggregationsAsync(
        string runId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus);

    /// <summary>
    ///     Updates test result aggregations and computed status for a suite.
    /// </summary>
    /// <param name="suiteId">Suite identifier.</param>
    /// <param name="totalTests">Total number of tests across all runs.</param>
    /// <param name="passedTests">Number of passed tests.</param>
    /// <param name="failedTests">Number of failed tests.</param>
    /// <param name="skippedTests">Number of skipped tests.</param>
    /// <param name="timedoutTests">Number of timed out tests.</param>
    /// <param name="computedStatus">Computed status string.</param>
    Task UpdateSuiteAggregationsAsync(
        Guid suiteId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus);

    /// <summary>
    ///     Updates test result aggregations and computed status for a launch.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <param name="totalTests">Total number of tests across all suites/runs.</param>
    /// <param name="passedTests">Number of passed tests.</param>
    /// <param name="failedTests">Number of failed tests.</param>
    /// <param name="skippedTests">Number of skipped tests.</param>
    /// <param name="timedoutTests">Number of timed out tests.</param>
    /// <param name="computedStatus">Computed status string.</param>
    Task UpdateLaunchAggregationsAsync(
        Guid launchId,
        int totalTests,
        int passedTests,
        int failedTests,
        int skippedTests,
        int timedoutTests,
        string computedStatus);

    /// <summary>
    ///     Gets all test runs for a specific suite.
    /// </summary>
    /// <param name="suiteId">Suite identifier.</param>
    /// <returns>List of test runs in the suite.</returns>
    Task<List<ResultRunSummaryDto>> GetTestRunsForSuiteAsync(Guid suiteId);

    /// <summary>
    ///     Gets all suites for a specific launch.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <returns>List of suites in the launch.</returns>
    Task<List<SuiteDto>> GetSuitesForLaunchAsync(Guid launchId);

    // ===== Artifact Storage =====

    /// <summary>
    ///     Saves an artifact to storage and returns its path.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <param name="testId">Test identifier.</param>
    /// <param name="fileName">Artifact filename.</param>
    /// <param name="content">Binary content of the artifact.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <returns>Storage path where artifact was saved.</returns>
    Task<string> SaveArtifactAsync(string runId, string testId, string fileName, byte[] content, string contentType);

    /// <summary>
    ///     Retrieves an artifact's binary content by its storage path.
    /// </summary>
    /// <param name="path">Storage path returned from SaveArtifactAsync.</param>
    /// <returns>Binary content or null if not found.</returns>
    Task<byte[]?> GetArtifactAsync(string path);

    /// <summary>
    ///     Lists all artifacts attached to a specific test case.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <param name="testId">Test identifier.</param>
    /// <returns>List of artifact metadata.</returns>
    Task<List<TestAttachmentDto>> GetArtifactsForTestAsync(string runId, string testId);

    /// <summary>
    ///     Deletes an artifact by its storage path.
    /// </summary>
    /// <param name="path">Storage path of the artifact to delete.</param>
    Task DeleteArtifactAsync(string path);

    /// <summary>
    ///     Gets all test runs for a specific launch.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <returns>List of test run summaries.</returns>
    Task<List<ResultRunSummaryDto>> GetRunsForLaunchAsync(Guid launchId);

    /// <summary>
    ///     Updates the last_activity timestamp for a launch to NOW().
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    Task UpdateLaunchActivityAsync(Guid launchId);

    /// <summary>
    ///     Recalculates launch aggregations (total, finished, running, stopped, errored counts)
    ///     based on current state of child runs.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    Task RecalculateLaunchAggregationsAsync(Guid launchId);

    /// <summary>
    ///     Recalculates and updates launch aggregations within an existing transaction.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <param name="conn">Existing database connection.</param>
    /// <param name="transaction">Existing transaction to use.</param>
    Task RecalculateLaunchAggregationsAsync(Guid launchId, NpgsqlConnection conn, NpgsqlTransaction transaction);

    /// <summary>
    ///     Gets all launches with InProgress status for a specific project,
    ///     ordered by last_activity ascending (oldest first).
    /// </summary>
    /// <param name="projectKey">Project key filter.</param>
    /// <param name="limit">Maximum number of launches to return.</param>
    /// <returns>List of launch DTOs.</returns>
    Task<List<LaunchDto>> GetInProgressLaunchesForProjectAsync(string projectKey, int limit = 100);

    /// <summary>
    ///     Updates launch status and finish time (for stopping launches).
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <param name="status">New status.</param>
    /// <param name="finishTime">Optional finish time.</param>
    Task UpdateLaunchStatusAsync(Guid launchId, string status, DateTimeOffset? finishTime = null);

    // ===== Test Item Hierarchy Methods ( Model) =====

    /// <summary>
    ///     Retrieves a single test item by its ID (without loading children).
    /// </summary>
    /// <param name="itemId">Item identifier (run_id).</param>
    /// <returns>Test item or null if not found.</returns>
    Task<TestItemDto?> GetTestItemAsync(Guid itemId);

    /// <summary>
    ///     Retrieves direct children of a test item (one level only).
    /// </summary>
    /// <param name="parentItemId">Parent item identifier.</param>
    /// <param name="itemType">Optional filter by item type (Test, Step, Suite, etc.).</param>
    /// <returns>List of child items ordered by start time.</returns>
    Task<List<TestItemDto>> GetChildItemsAsync(Guid parentItemId, string? itemType = null);

    /// <summary>
    ///     Retrieves a test item with its entire child hierarchy (recursive tree structure).
    /// </summary>
    /// <param name="itemId">Root item identifier.</param>
    /// <param name="maxDepth">Maximum depth to load (default 5, prevents infinite recursion).</param>
    /// <returns>Test item with Children property populated recursively.</returns>
    Task<TestItemDto?> GetTestItemWithChildrenAsync(Guid itemId, int maxDepth = 5);

    /// <summary>
    ///     Retrieves all test items for a launch, optionally filtered by item type.
    /// </summary>
    /// <param name="launchId">Launch identifier.</param>
    /// <param name="itemType">Optional filter by item type (Test, Step, Scenario, etc.).</param>
    /// <returns>Flat list of test items (no hierarchy loaded).</returns>
    Task<List<TestItemDto>> GetTestItemsForLaunchAsync(Guid launchId, string? itemType = null);

    /// <summary>
    ///     Retrieves test execution history across multiple launches.
    /// </summary>
    /// <param name="uniqueIdOrName">Unique test identifier or test name.</param>
    /// <param name="itemType">Test item type (Test, Scenario, etc.).</param>
    /// <param name="limit">Maximum number of history items to return.</param>
    /// <returns>List of test executions ordered by launch number (descending).</returns>
    Task<List<TestHistoryItemDto>> GetTestItemHistoryAsync(string uniqueIdOrName, string itemType, int limit);

    /// <summary>
    ///     Gets all log items for a specific test item.
    /// </summary>
    Task<List<LogItemDto>> GetLogItemsForTestItemAsync(Guid testItemId);

    /// <summary>
    ///     Gets hierarchical log items with step headers for a test item.
    ///     Combines test item children (steps) with their associated log items.
    /// </summary>
    /// <param name="testItemId">The test item ID to get logs for.</param>
    /// <param name="skip">Number of log items to skip for pagination.</param>
    /// <param name="take">Number of log items to take for pagination.</param>
    /// <returns>List of hierarchical log entries including step headers and nested logs.</returns>
    Task<List<HierarchicalLogEntryDto>> GetLogItemsWithStepsAsync(Guid testItemId, int skip = 0, int take = 1000);

    /// <summary>
    ///     Retrieves all test items for a suite, optionally filtered by item type.
    /// </summary>
    /// <param name="suiteId">Suite identifier.</param>
    /// <param name="itemType">Optional filter by item type (Test, Step, Scenario, etc.).</param>
    /// <returns>Flat list of test items (no hierarchy loaded).</returns>
    Task<List<TestItemDto>> GetTestItemsForSuiteAsync(Guid suiteId, string? itemType = null);

    /// <summary>
    ///     Retrieves active test items with browser sessions, filtered by session status and item type.
    ///     Used by BrowserCleanupService to identify items that need cleanup.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="sessionStatuses">Session statuses to filter by (e.g., 'Queued', 'Running').</param>
    /// <param name="itemTypes">Optional item types to filter by (e.g., 'Test', 'Scenario'). Defaults to Test and Scenario.</param>
    /// <returns>List of test items with active browser sessions.</returns>
    Task<List<TestItemDto>> GetActiveTestItemsAsync(
        int skip,
        int take,
        string[] sessionStatuses,
        string[]? itemTypes = null);

    // ========================================
    // Log Items
    // ========================================

    /// <summary>
    ///     Creates a new log item and returns its ID.
    /// </summary>
    /// <param name="dto">The log item data to create.</param>
    /// <returns>The UUID of the created log item.</returns>
    Task<Guid> CreateLogItemAsync(CreateLogItemDto dto);

    /// <summary>
    ///     Creates multiple log items in a single batch operation.
    ///     Uses token deduplication and optimized bulk insert.
    /// </summary>
    Task<List<Guid>> CreateLogItemBatchAsync(List<CreateLogItemDto> dtos);

    /// <summary>
    ///     Retrieves a log item by its ID.
    /// </summary>
    /// <param name="id">The log item UUID.</param>
    /// <returns>The log item or null if not found.</returns>
    Task<LogItemDto?> GetLogItemAsync(Guid id);

    /// <summary>
    ///     Retrieves log items for a specific test item.
    /// </summary>
    /// <param name="testItemUuid">The test item UUID.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <returns>List of log items ordered by time descending.</returns>
    Task<List<LogItemDto>> GetLogItemsForTestItemAsync(Guid testItemUuid, int skip = 0, int take = 100);

    /// <summary>
    ///     Retrieves log items for a specific launch.
    /// </summary>
    /// <param name="launchUuid">The launch UUID.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <returns>List of log items ordered by time descending.</returns>
    Task<List<LogItemDto>> GetLogItemsForLaunchAsync(Guid launchUuid, int skip = 0, int take = 100);

    /// <summary>
    ///     Retrieves artifact metadata by its unique identifier.
    /// </summary>
    /// <param name="artifactId">The artifact UUID.</param>
    /// <returns>Artifact metadata or null if not found.</returns>
    Task<ArtifactMetadata?> GetArtifactAsync(Guid artifactId);

    /// <summary>
    ///     Creates artifact metadata with "pending" status and returns artifact ID.
    ///     Actual file upload is handled asynchronously by ingestion service.
    ///     Call this method then publish ArtifactUploadEvent via IEventPublisher.
    /// </summary>
    /// <param name="testItemId">Test item UUID the artifact belongs to.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="fileSize">File size in bytes.</param>
    /// <param name="projectKey">Project key for retention policies.</param>
    /// <returns>The generated artifact UUID.</returns>
    Task<Guid> CreateArtifactMetadataAsync(
        Guid testItemId,
        string fileName,
        string contentType,
        long fileSize,
        string projectKey);
}

/// <summary>
///     Artifact metadata record containing file information and storage path.
///     Used for downloading artifacts from MinIO or local filesystem.
/// </summary>
public record ArtifactMetadata(
    Guid Id,
    Guid TestItemId,
    string FileName,
    string ContentType,
    long FileSize,
    string StoragePath,
    DateTime UploadedAt
);
