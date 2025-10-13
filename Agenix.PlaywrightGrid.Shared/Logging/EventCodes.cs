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
///     Stable event code catalog for milestone logging.
///     Event codes provide searchable, stable identifiers for key operations,
///     making it easy to track specific events across logs and create alerts.
/// </summary>
public static class EventCodes
{
    /// <summary>
    ///     Generic/fallback event code for unclassified events.
    /// </summary>
    public const string Generic = "GEN00";

    /// <summary>
    ///     Gets the title/description for an event code (for display purposes).
    /// </summary>
    public static string GetEventTitle(string eventCode)
    {
        return eventCode switch
        {
            // Browser pool
            BrowserPool.BorrowRequested => "Browser borrow requested",
            BrowserPool.BrowserAllocated => "Browser allocated",
            BrowserPool.BrowserReady => "Browser ready",
            BrowserPool.BorrowFailed => "Browser borrow failed",
            BrowserPool.BorrowTimeout => "Browser borrow timeout",
            BrowserPool.ReturnRequested => "Browser return requested",
            BrowserPool.BrowserReturned => "Browser returned to pool",
            BrowserPool.ReturnFailed => "Browser return failed",
            BrowserPool.ScanStarted => "Cleanup scan started",
            BrowserPool.BrowserReleased => "Browser released",
            BrowserPool.ItemAutoStopped => "Test item auto-stopped",
            BrowserPool.CleanupFailed => "Cleanup failed",

            // Launch
            Launch.LaunchCreated => "Launch created",
            Launch.LaunchStarted => "Launch started",
            Launch.LaunchFinished => "Launch finished",
            Launch.LaunchFailed => "Launch failed",
            Launch.LaunchNotFound => "Launch not found",
            Launch.LaunchCreationFailed => "Launch creation failed",
            Launch.LaunchAlreadyFinished => "Launch already finished",
            Launch.LaunchForceFinished => "Launch force-finished",
            Launch.LaunchOperationFailed => "Launch operation failed",
            Launch.StatusCalculated => "Launch status calculated",
            Launch.AggregationsUpdated => "Launch aggregations updated",
            Launch.AutoStopTriggered => "Launch auto-stop triggered",

            // Finish launch workflow
            Launch.FinishLaunchStarted => "Finish launch started",
            Launch.AuthorizationStarted => "Authorization started",
            Launch.TerminalStateCheckStarted => "Terminal state check started",
            Launch.StatusCalculationStarted => "Status calculation started",
            Launch.StatusCalculationFallback => "Status calculation fallback",
            Launch.LaunchUpdateStarted => "Launch update started",
            Launch.LaunchUpdated => "Launch updated",
            Launch.CacheInvalidationStarted => "Cache invalidation started",
            Launch.CacheInvalidated => "Cache invalidated",
            Launch.CacheInvalidationFailed => "Cache invalidation failed",

            // Delete launch workflow
            Launch.DeleteLaunchStarted => "Delete launch started",
            Launch.DeleteAuthorizationStarted => "Delete authorization started",
            Launch.DeleteTransactionStarted => "Delete transaction started",
            Launch.LaunchDeleted => "Launch deleted",
            Launch.DeleteLaunchCompleted => "Delete launch completed",

            // Launch data
            Launch.AttributesUpdated => "Launch attributes updated",
            Launch.DescriptionUpdated => "Launch description updated",
            Launch.MetadataUpdated => "Launch metadata updated",

            // Force finish launch workflow
            Launch.ForceFinishStarted => "Force finish started",
            Launch.ForceFinishLaunchStatusChecked => "Force finish launch status checked",
            Launch.ActiveTestItemsFound => "Active test items found",
            Launch.TestItemStopped => "Test item stopped",
            Launch.BrowserReleased => "Browser released",
            Launch.ForceFinishLaunchStatusUpdated => "Force finish launch status updated",
            Launch.ForceFinishAggregationsRecalculated => "Force finish aggregations recalculated",
            Launch.ForceFinishAuditLogged => "Force finish audit logged",
            Launch.ForceFinishCacheInvalidated => "Force finish cache invalidated",
            Launch.ForceFinishCompleted => "Force finish completed",

            // Log item
            LogItem.LogItemCreated => "Log item created",
            LogItem.LogItemCreationFailed => "Log item creation failed",
            LogItem.LogItemBatchCreated => "Log item batch created",
            LogItem.LogItemBatchFailed => "Log item batch failed",
            LogItem.LogItemRetrieved => "Log item retrieved",
            LogItem.LogItemRetrievalFailed => "Log item retrieval failed",
            LogItem.LogItemsForTestItemRetrieved => "Log items for test item retrieved",
            LogItem.LogItemsForLaunchRetrieved => "Log items for launch retrieved",
            LogItem.LogItemBatchValidationStarted => "Log item batch validation started",
            LogItem.LogItemBatchValidationComplete => "Log item batch validation complete",
            LogItem.LogItemBatchInvalidItemsSkipped => "Log item batch invalid items skipped",
            LogItem.TestItemLookupStarted => "Test item lookup started",
            LogItem.TestItemFound => "Test item found",
            LogItem.LogItemEventPublished => "Log item event published",
            LogItem.LogItemEventPublishFailed => "Log item event publish failed",
            LogItem.LogItemBatchEventsPublished => "Log item batch events published",
            LogItem.LogItemEventPublishConfirmed => "Log item event publish confirmed",
            LogItem.BatchProcessingStarted => "Batch processing started",
            LogItem.LogItemQueryExecuted => "Log item query executed",
            LogItem.LogItemQueryFailed => "Log item query failed",
            LogItem.QueryCompleted => "Query completed",
            LogItem.QueryForLaunchCompleted => "Query for launch completed",

            // Test item
            TestItem.ItemCreated => "Test item created",
            TestItem.ItemPersisted => "Test item persisted to database",
            TestItem.ItemStarted => "Test item started",
            TestItem.ItemFinished => "Test item finished",
            TestItem.ItemFailed => "Test item failed",
            TestItem.TestItemNotFound => "Test item not found",
            TestItem.TestItemCreationFailed => "Test item creation failed",
            TestItem.TestItemOperationFailed => "Test item operation failed",
            TestItem.LogAdded => "Log item added",
            TestItem.ArtifactUploaded => "Artifact uploaded",
            TestItem.StatusUpdated => "Status updated",

            // Ingestion
            Ingestion.BatchReceived => "Batch received from RabbitMQ",
            Ingestion.BatchStarted => "Batch processing started",
            Ingestion.BatchCompleted => "Batch processing completed",
            Ingestion.TestItemsWritten => "Test items written to database",
            Ingestion.LogItemsWritten => "Log items written to database",
            Ingestion.TokenCreated => "Log token created",

            // Worker
            Worker.BrowserStartupRequested => "Browser startup requested",
            Worker.PlaywrightLaunched => "Playwright launched",
            Worker.BrowserConnected => "Browser connected",
            Worker.BrowserStartupFailed => "Browser startup failed",
            Worker.SidecarReady => "Sidecar ready",
            Worker.SidecarExited => "Sidecar process exited",
            Worker.BrowserBorrowed => "Browser borrowed by client",
            Worker.BrowserReturned => "Browser returned by client",
            Worker.PoolWarmingStarted => "Browser pool warming started",
            Worker.PoolWarmingCompleted => "Browser pool warming completed",
            Worker.PoolResizeStarted => "Pool resize started",
            Worker.PoolResizeCompleted => "Pool resize completed",
            Worker.ReconcileStarted => "Browser pool reconciliation started",
            Worker.ReconcileCompleted => "Browser pool reconciliation completed",
            Worker.CleanupRequested => "Cleanup requested",
            Worker.BrowserClosed => "Browser closed",
            Worker.CleanupFailed => "Cleanup failed",
            Worker.OrphanCleanupStarted => "Orphaned processes cleanup started",
            Worker.OrphanCleanupCompleted => "Orphaned processes cleanup completed",
            Worker.LabelRemoved => "Pool label removed",
            Worker.BrowserPruned => "Browser pruned from pool",
            Worker.RegistrationStarted => "Worker registration started",
            Worker.RegistrationSent => "Worker registration sent to hub",
            Worker.RegistrationConfirmed => "Worker registration confirmed",
            Worker.RegistrationFailed => "Worker registration failed",
            Worker.RegistrationVerificationStarted => "Worker registration verification started",
            Worker.RegistrationVerificationSucceeded => "Worker registration verification succeeded",
            Worker.RegistrationVerificationFailed => "Worker registration verification failed",

            // Node
            Node.RegistrationReceived => "Node registration received",
            Node.RegistrationSuccess => "Node registration success",
            Node.RegistrationUpdate => "Node registration update",
            Node.RegistrationFailed => "Node registration failed",
            Node.RegistrationCleanup => "Node registration cleanup",

            // Storage
            Storage.UploadStarted => "Upload started",
            Storage.UploadCompleted => "Upload completed",
            Storage.UploadFailed => "Upload failed",
            Storage.DownloadStarted => "Download started",
            Storage.DownloadCompleted => "Download completed",
            Storage.UrlGenerated => "Pre-signed URL generated",

            // Housekeeping
            Housekeeping.LaunchRetentionStarted => "Launch retention check started",
            Housekeeping.LaunchesDeleted => "Launches deleted",
            Housekeeping.LaunchRetentionCompleted => "Launch retention check completed",
            Housekeeping.LogRetentionStarted => "Log retention check started",
            Housekeeping.LogItemsDeleted => "Log items deleted",
            Housekeeping.OrphanedTokensCleaned => "Orphaned tokens cleaned",
            Housekeeping.LogRetentionCompleted => "Log retention check completed",
            Housekeeping.ArtifactRetentionStarted => "Artifact retention check started",
            Housekeeping.ArtifactsDeleted => "Artifacts deleted",
            Housekeeping.PhysicalFilesDeleted => "Physical files deleted",
            Housekeeping.ArtifactRetentionCompleted => "Artifact retention check completed",
            Housekeeping.AuditRetentionStarted => "Audit retention check started",
            Housekeeping.AuditEntriesDeleted => "Audit entries deleted",
            Housekeeping.AuditRetentionCompleted => "Audit retention check completed",
            Housekeeping.LaunchAutoStopStarted => "Launch auto-stop check started",
            Housekeeping.LaunchAutoStopped => "Launch auto-stopped",
            Housekeeping.LaunchAutoStopCompleted => "Launch auto-stop check completed",

            // Database - Migrations
            Database.MigrationStarted => "Database migration started",
            Database.DatabaseConnectionTested => "Database connection tested",
            Database.DatabaseReady => "Database ready",
            Database.MigrationUpgradeCheckStarted => "Migration upgrade check started",
            Database.MigrationUpgradeNotRequired => "Migration upgrade not required",
            Database.MigrationUpgradeRequired => "Migration upgrade required",
            Database.MigrationScriptsDiscovered => "Migration scripts discovered",
            Database.MigrationScriptStarting => "Migration script starting",
            Database.MigrationScriptExecuting => "Migration script executing",
            Database.MigrationScriptCompleted => "Migration script completed",
            Database.MigrationTransactionStarted => "Migration transaction started",
            Database.MigrationTransactionCommitted => "Migration transaction committed",
            Database.MigrationTransactionRolledBack => "Migration transaction rolled back",
            Database.MigrationVerificationStarted => "Migration verification started",
            Database.MigrationVerificationCompleted => "Migration verification completed",
            Database.MigrationVersionJournalUpdated => "Migration version journal updated",
            Database.MigrationCompletedSuccessfully => "Migration completed successfully",
            Database.MigrationConnectionFailed => "Migration connection failed",
            Database.MigrationFailed => "Migration failed",

            // Database - Legacy (MigrationApplied is an alias for MigrationScriptCompleted - both are "DB10")
            // No separate case needed since switch expressions match by value, not by constant name

            // Database - Connections
            Database.ConnectionOpened => "Connection opened",
            Database.ConnectionClosed => "Connection closed",
            Database.ConnectionPoolExhausted => "Connection pool exhausted",
            Database.ConnectionRetry => "Connection retry",

            // Database - Transactions
            Database.TransactionStarted => "Transaction started",
            Database.TransactionCommitted => "Transaction committed",
            Database.TransactionRolledBack => "Transaction rolled back",
            Database.TransactionFailed => "Transaction failed",
            Database.OperationFailed => "Database operation failed",

            // Redis
            Redis.ClientInitialized => "Redis client initialized",
            Redis.Connected => "Redis connection connected",
            Redis.ConnectionError => "Redis connection error",
            Redis.KeyOperationSuccess => "Redis key operation success",
            Redis.KeyOperationFailed => "Redis key operation failed",
            Redis.SetOperationSuccess => "Redis set operation success",
            Redis.SetOperationFailed => "Redis set operation failed",
            Redis.TransactionStarted => "Redis transaction started",
            Redis.TransactionCommitted => "Redis transaction committed",
            Redis.TransactionFailed => "Redis transaction failed",
            Redis.HeartbeatSent => "Redis heartbeat sent",
            Redis.OperationFailed => "Redis operation failed",

            // Admin Query
            AdminProjectsUsers.Query.ProjectListRetrieved => "Project list retrieved",
            AdminProjectsUsers.Query.ProjectDetailsRetrieved => "Project details retrieved",
            AdminProjectsUsers.Query.UserListRetrieved => "User list retrieved",
            AdminProjectsUsers.Query.UserDetailsRetrieved => "User details retrieved",
            AdminProjectsUsers.Query.MembershipListRetrieved => "Membership list retrieved",

            // Admin User Management
            AdminProjectsUsers.Authentication.LoginAttempt => "Login attempt",
            AdminProjectsUsers.Authentication.LoginSucceeded => "Login succeeded",
            AdminProjectsUsers.Authentication.LoginFailed => "Login failed",
            AdminProjectsUsers.Authentication.LoginRateLimitExceeded => "Login rate limit exceeded",
            AdminProjectsUsers.Authentication.Logout => "Logout",
            AdminProjectsUsers.UserManagement.UserCreated => "User created",
            AdminProjectsUsers.UserManagement.UserUpdated => "User updated",
            AdminProjectsUsers.UserManagement.UserDeleted => "User deleted",
            AdminProjectsUsers.UserManagement.UserActivated => "User activated",
            AdminProjectsUsers.UserManagement.UserDeactivated => "User deactivated",
            AdminProjectsUsers.UserManagement.UserPasswordReset => "User password reset",

            // Admin Project Management
            AdminProjectsUsers.ProjectManagement.ProjectCreated => "Project created",
            AdminProjectsUsers.ProjectManagement.ProjectUpdated => "Project updated",
            AdminProjectsUsers.ProjectManagement.ProjectDeleted => "Project deleted",
            AdminProjectsUsers.ProjectManagement.ProjectArchived => "Project archived",
            AdminProjectsUsers.ProjectManagement.ProjectRestored => "Project restored",

            // Admin Membership Management
            AdminProjectsUsers.MembershipManagement.MembershipAdded => "Membership added",
            AdminProjectsUsers.MembershipManagement.MembershipRemoved => "Membership removed",
            AdminProjectsUsers.MembershipManagement.MembershipRoleUpdated => "Membership role updated",

            // Admin Invitations
            AdminProjectsUsers.AdminUserManagement.AdminInvited => "Admin invited",
            AdminProjectsUsers.AdminUserManagement.InviteAccepted => "Invite accepted",
            AdminProjectsUsers.AdminUserManagement.InviteExpired => "Invite expired",
            AdminProjectsUsers.AdminUserManagement.InviteRevoked => "Invite revoked",

            // Node Sweeper
            NodeSweeper.LeaderElectionStarted => "Leader election started",
            NodeSweeper.LeaderLockAcquired => "Leader lock acquired",
            NodeSweeper.LeaderLockRenewed => "Leader lock renewed",
            NodeSweeper.LeaderLockFailed => "Leader lock failed",
            NodeSweeper.ScanningStarted => "Scanning started",
            NodeSweeper.NodesRetrieved => "Nodes retrieved",
            NodeSweeper.NodeProcessingStarted => "Node processing started",
            NodeSweeper.NodeSkippedHealthy => "Node skipped (healthy)",
            NodeSweeper.NodeExpired => "Node expired",
            NodeSweeper.NodeQuarantined => "Node quarantined",
            NodeSweeper.NodeExpiredRemoved => "Node expired and removed",
            NodeSweeper.AvailableEntriesPruned => "Available entries pruned",
            NodeSweeper.InuseEntriesPruned => "In-use entries pruned",
            NodeSweeper.NodeProcessingFailed => "Node processing failed",
            NodeSweeper.ScanningFailed => "Scanning failed",
            NodeSweeper.RedisTimeout => "Redis timeout",
            NodeSweeper.ScanningCompleted => "Scanning completed",
            NodeSweeper.QuarantineGaugeUpdated => "Quarantine gauge updated",

            // Orphan Detector
            OrphanDetector.LeaderElectionStarted => "Leader election started",
            OrphanDetector.LeaderLockAcquired => "Leader lock acquired",
            OrphanDetector.LeaderLockRenewed => "Leader lock renewed",
            OrphanDetector.LeaderLockReleased => "Leader lock released",
            OrphanDetector.ScanningStarted => "Orphan scanning started",
            OrphanDetector.ScanningHeartbeatExpired => "Heartbeat expired",
            OrphanDetector.ScanningOrphanedPidsFound => "Orphaned PIDs found",
            OrphanDetector.ScanningComplete => "Orphan scanning complete",
            OrphanDetector.PidCleanupStarted => "PID cleanup started",
            OrphanDetector.PidCleaned => "PID cleaned",
            OrphanDetector.PidCleanupFailed => "PID cleanup failed",
            OrphanDetector.WorkerKeysCleanupStarted => "Worker keys cleanup started",
            OrphanDetector.WorkerKeysCleaned => "Worker keys cleaned",
            OrphanDetector.DetectFailed => "Orphan detection failed",
            OrphanDetector.LeaderLockFailed => "Leader lock failed",

            // Event Publisher
            EventPublisher.TestItemPublished => "Test item published",
            EventPublisher.CommandPublished => "Command published",
            EventPublisher.LogItemPublished => "Log item published",
            EventPublisher.AuditPublished => "Audit published",
            EventPublisher.ArtifactPublished => "Artifact published",
            EventPublisher.BatchPublished => "Batch published",
            EventPublisher.BatchPartialFailure => "Batch partial failure",
            EventPublisher.MessageSizeLogged => "Message size logged",
            EventPublisher.PublishFailed => "Publish failed",
            EventPublisher.ConnectionLost => "Connection lost",
            EventPublisher.RetryAttempt => "Retry attempt",
            EventPublisher.RetryFailed => "Retry failed",
            EventPublisher.ChannelCreated => "Channel created",
            EventPublisher.ChannelClosed => "Channel closed",
            EventPublisher.ExchangeDeclared => "Exchange declared",
            EventPublisher.QueueDeclared => "Queue declared",
            EventPublisher.AdditionalQueueDeclared => "Additional queue declared",
            EventPublisher.PublishConfirmed => "Publish confirmed",

            // Borrow TTL Sweeper
            BorrowTtlSweeper.ScanStarted => "Scan started",
            BorrowTtlSweeper.ScanCompleted => "Scan completed",
            BorrowTtlSweeper.SessionKeyFound => "Session key found",
            BorrowTtlSweeper.TtlCheckStarted => "TTL check started",
            BorrowTtlSweeper.TtlExpired => "TTL expired",
            BorrowTtlSweeper.TtlStillValid => "TTL still valid",
            BorrowTtlSweeper.SessionMetadataLoaded => "Session metadata loaded",
            BorrowTtlSweeper.SessionMetadataEmpty => "Session metadata empty",
            BorrowTtlSweeper.BrowserReturnStarted => "Browser return started",
            BorrowTtlSweeper.BrowserReturned => "Browser returned",
            BorrowTtlSweeper.BrowserReturnFailed => "Browser return failed",
            BorrowTtlSweeper.CommandEventPublished => "Command event published",
            BorrowTtlSweeper.SignalRNotificationSent => "SignalR notification sent",
            BorrowTtlSweeper.EventPublished => "Event published",
            BorrowTtlSweeper.RunAutoStopped => "Run auto-stopped",
            BorrowTtlSweeper.BatchProcessed => "Batch processed",

            // Browser Auto-Stop
            BrowserAutoStop.ScanStarted => "Scan started",
            BrowserAutoStop.ActiveItemsRetrieved => "Active items retrieved",
            BrowserAutoStop.ItemSelected => "Item selected for auto-stop",
            BrowserAutoStop.InactivityCheckStarted => "Inactivity check started",
            BrowserAutoStop.InactivityMet => "Inactivity threshold met",
            BrowserAutoStop.DurationCheckStarted => "Duration check started",
            BrowserAutoStop.DurationExceeded => "Max duration exceeded",
            BrowserAutoStop.CommandLogAnalysisStarted => "Command log analysis started",
            BrowserAutoStop.LaunchCommandFound => "Launch command found",
            BrowserAutoStop.ReturnCommandFound => "Return command found",
            BrowserAutoStop.OutstandingBrowsersDetected => "Outstanding browsers detected",
            BrowserAutoStop.BrowserReturnStarted => "Browser return started",
            BrowserAutoStop.BrowserReturned => "Browser returned",
            BrowserAutoStop.BrowserReturnFailed => "Browser return failed",
            BrowserAutoStop.SignalRNotificationSent => "SignalR notification sent",
            BrowserAutoStop.EventPublished => "Event published",
            BrowserAutoStop.ItemProcessed => "Item processed",
            BrowserAutoStop.BatchCompleted => "Batch completed",

            // Browser Health Checker
            BrowserHealth.LoopStarted => "Browser health check loop started",
            BrowserHealth.LoopCompleted => "Browser health check loop completed",
            BrowserHealth.LoopError => "Browser health check loop error",
            BrowserHealth.CheckStarted => "Individual browser health check started",
            BrowserHealth.CheckPassed => "Individual browser health check passed",
            BrowserHealth.CheckFailed => "Individual browser health check failed",
            BrowserHealth.CheckException => "Individual browser health check exception",
            BrowserHealth.RecycleTriggered => "Browser recycle triggered",
            BrowserHealth.RecycleFailed => "Browser recycle trigger failed",

            // Heartbeat
            Worker.HeartbeatStarted => "Heartbeat loop started",
            Worker.HeartbeatTick => "Heartbeat tick done",
            Worker.HeartbeatFailed => "Heartbeat tick failed",
            Worker.HeartbeatGapDetected => "Heartbeat timer gap detected",
            Worker.HeartbeatStopped => "Heartbeat loop stopped",

            // Artifacts
            Artifacts.ArtifactUploaded => "Artifact uploaded",
            Artifacts.ArtifactUploadStarted => "Artifact upload started",
            Artifacts.ArtifactUploadCompleted => "Artifact upload completed",
            Artifacts.ArtifactUploadFailed => "Artifact upload failed",
            Artifacts.ArtifactDownloaded => "Artifact downloaded",
            Artifacts.ArtifactDownloadStarted => "Artifact download started",
            Artifacts.ArtifactDownloadCompleted => "Artifact download completed",
            Artifacts.ArtifactDownloadFailed => "Artifact download failed",
            Artifacts.ArtifactListed => "Artifact listed",
            Artifacts.ArtifactListedBatch => "Artifact listed batch",
            Artifacts.ArtifactUrlGenerated => "Artifact URL generated",
            Artifacts.ArtifactBatchZipCreated => "Artifact batch zip created",
            Artifacts.ArtifactBatchZipFailed => "Artifact batch zip failed",
            Artifacts.ArtifactCacheHit => "Artifact cache hit",
            Artifacts.ArtifactCacheMiss => "Artifact cache miss",
            Artifacts.ArtifactStorageError => "Artifact storage error",

            // Project Settings
            ProjectSettings.SettingsRetrieved => "Settings retrieved",
            ProjectSettings.SettingsMissing => "Settings missing",
            ProjectSettings.SettingsUpdated => "Settings updated",
            ProjectSettings.SettingsValidationStarted => "Settings validation started",
            ProjectSettings.SettingsValidationFailed => "Settings validation failed",
            ProjectSettings.SettingsValidationSucceeded => "Settings validation succeeded",
            ProjectSettings.RetentionValuesValidated => "Retention values validated",
            ProjectSettings.RetentionValueInvalid => "Retention value invalid",
            ProjectSettings.RetentionHierarchyViolation => "Retention hierarchy violation",
            ProjectSettings.SettingsPersisted => "Settings persisted",
            ProjectSettings.SettingsPersistenceFailed => "Settings persistence failed",
            ProjectSettings.ConfigurationLoaded => "Configuration loaded",
            ProjectSettings.ConfigurationMissing => "Configuration missing",

            // Password Reset
            PasswordReset.ResetRequested => "Password reset requested",
            PasswordReset.ResetRateLimitExceeded => "Reset rate limit exceeded",
            PasswordReset.ResetRequestDenied => "Reset request denied",
            PasswordReset.TokenGenerated => "Reset token generated",
            PasswordReset.TokenValidated => "Reset token validated",
            PasswordReset.TokenExpired => "Reset token expired",
            PasswordReset.TokenInvalid => "Reset token invalid",
            PasswordReset.PasswordResetCompleted => "Password reset completed",
            PasswordReset.PasswordResetFailed => "Password reset failed",
            PasswordReset.EmailSent => "Reset email sent",
            PasswordReset.EmailSendFailed => "Reset email send failed",
            PasswordReset.RateLimitIncremented => "Rate limit incremented",

            // System
            System.BootstrapStarted => "System bootstrap started",
            System.BootstrapCompleted => "System bootstrap completed",
            System.BootstrapFailed => "System bootstrap failed",
            System.BootstrapUserCreated => "Bootstrap admin user created",
            System.BootstrapProjectCreated => "Bootstrap default project created",
            System.BootstrapMembershipCreated => "Bootstrap membership created",
            System.BootstrapSettingsInitialized => "Bootstrap settings initialized",

            // Web Server
            WebServer.ServerStarting => "Web server starting",
            WebServer.ServerStarted => "Web server started",
            WebServer.ServerStopping => "Web server stopping",
            WebServer.ServerStopped => "Web server stopped",
            WebServer.EndpointsRegistered => "Endpoints registered",
            WebServer.ConfigurationDumped => "Configuration dumped",
            WebServer.ListeningAddresses => "Listening addresses",
            WebServer.RequestReceived => "Request received",
            WebServer.RequestProcessed => "Request processed",
            WebServer.RequestFailed => "Request failed",

            // Default
            _ => eventCode
        };
    }

    /// <summary>
    /// Admin, project, and user management related event codes (ADM01-ADM99).
    /// These codes track administrative operations, user lifecycle, and project management activities.
    /// </summary>
    public static class AdminProjectsUsers
    {
        /// <summary>
        /// Authentication and authorization related event codes (ADM01-ADM09).
        /// Track login attempts, successes, failures, and rate limiting.
        /// </summary>
        public static class Authentication
        {
            /// <summary>Event fired when a login attempt is initiated (ADM01)</summary>
            public const string LoginAttempt = "ADM01";

            /// <summary>Event fired when a login attempt succeeds (ADM02)</summary>
            public const string LoginSucceeded = "ADM02";

            /// <summary>Event fired when a login attempt fails (ADM03)</summary>
            public const string LoginFailed = "ADM03";

            /// <summary>Event fired when login rate limit is exceeded (ADM04)</summary>
            public const string LoginRateLimitExceeded = "ADM04";

            /// <summary>Event fired when a logout occurs (ADM05)</summary>
            public const string Logout = "ADM05";
        }

        /// <summary>
        /// Query and retrieval event codes (ADM11-ADM19).
        /// Track listing and detail retrieval for projects and users.
        /// </summary>
        public static class Query
        {
            /// <summary>Event fired when project list is retrieved (ADM11)</summary>
            public const string ProjectListRetrieved = "ADM11";

            /// <summary>Event fired when project details are retrieved (ADM12)</summary>
            public const string ProjectDetailsRetrieved = "ADM12";

            /// <summary>Event fired when user list is retrieved (ADM13)</summary>
            public const string UserListRetrieved = "ADM13";

            /// <summary>Event fired when user details are retrieved (ADM14)</summary>
            public const string UserDetailsRetrieved = "ADM14";

            /// <summary>Event fired when membership list is retrieved (ADM15)</summary>
            public const string MembershipListRetrieved = "ADM15";
        }

        /// <summary>
        /// User account management event codes (ADM21-ADM29).
        /// Track user creation, updates, deletions, and password management.
        /// </summary>
        public static class UserManagement
        {
            /// <summary>Event fired when a new user is created (ADM21)</summary>
            public const string UserCreated = "ADM21";

            /// <summary>Event fired when existing user is updated (ADM22)</summary>
            public const string UserUpdated = "ADM22";

            /// <summary>Event fired when user is deleted (ADM23)</summary>
            public const string UserDeleted = "ADM23";

            /// <summary>Event fired when deactivated user is reactivated (ADM24)</summary>
            public const string UserActivated = "ADM24";

            /// <summary>Event fired when active user is deactivated (ADM25)</summary>
            public const string UserDeactivated = "ADM25";

            /// <summary>Event fired when user password is reset (ADM26)</summary>
            public const string UserPasswordReset = "ADM26";
        }

        /// <summary>
        /// Project lifecycle management event codes (ADM31-ADM39).
        /// Track project creation, updates, deletion, archival, and restoration.
        /// </summary>
        public static class ProjectManagement
        {
            /// <summary>Event fired when new project is created (ADM31)</summary>
            public const string ProjectCreated = "ADM31";

            /// <summary>Event fired when existing project is updated (ADM32)</summary>
            public const string ProjectUpdated = "ADM32";

            /// <summary>Event fired when project is deleted (ADM33)</summary>
            public const string ProjectDeleted = "ADM33";

            /// <summary>Event fired when project is archived (ADM34)</summary>
            public const string ProjectArchived = "ADM34";

            /// <summary>Event fired when archived project is restored (ADM35)</summary>
            public const string ProjectRestored = "ADM35";
        }

        /// <summary>
        /// Project membership and role management event codes (ADM41-ADM49).
        /// Track when users are added/removed from projects and role changes.
        /// </summary>
        public static class MembershipManagement
        {
            /// <summary>Event fired when user is added to project (ADM41)</summary>
            public const string MembershipAdded = "ADM41";

            /// <summary>Event fired when user is removed from project (ADM42)</summary>
            public const string MembershipRemoved = "ADM42";

            /// <summary>Event fired when user's project role is updated (ADM43)</summary>
            public const string MembershipRoleUpdated = "ADM43";
        }

        /// <summary>
        /// Administrator invitation and onboarding event codes (ADM51-ADM59).
        /// Track admin invitations, acceptances, expiration, and revocation.
        /// </summary>
        public static class AdminUserManagement
        {
            /// <summary>Event fired when admin invitation is sent (ADM51)</summary>
            public const string AdminInvited = "ADM51";

            /// <summary>Event fired when admin invitation is accepted (ADM52)</summary>
            public const string InviteAccepted = "ADM52";

            /// <summary>Event fired when admin invitation expires (ADM53)</summary>
            public const string InviteExpired = "ADM53";

            /// <summary>Event fired when admin invitation is revoked (ADM54)</summary>
            public const string InviteRevoked = "ADM54";
        }

        /// <summary>
        /// Input validation event codes (ADM91-ADM92).
        /// Track validation failures and successes for audit trails.
        /// </summary>
        public static class Validation
        {
            /// <summary>Event fired when input validation fails (ADM91)</summary>
            public const string ValidationFailed = "ADM91";

            /// <summary>Event fired when input validation succeeds (ADM92)</summary>
            public const string ValidationSucceeded = "ADM92";
        }

        /// <summary>
        /// Rate limiting and throttling event codes (ADM95-ADM97).
        /// Track when operations hit rate limits for security monitoring.
        /// </summary>
        public static class RateLimiting
        {
            /// <summary>Event fired when general rate limit is exceeded (ADM95)</summary>
            public const string RateLimitExceeded = "ADM95";

            /// <summary>Event fired when API rate limit is exceeded (ADM96)</summary>
            public const string ApiRateLimitExceeded = "ADM96";

            /// <summary>Event fired when project creation rate limit is exceeded (ADM97)</summary>
            public const string ProjectCreationRateLimitExceeded = "ADM97";
        }
    }

    /// <summary>
    ///     Browser pool operations (POOL01-POOL99)
    /// </summary>
    public static class BrowserPool
    {
        // Borrowing flow
        public const string BorrowRequested = "POOL01";
        public const string BrowserAllocated = "POOL02";
        public const string BrowserReady = "POOL03";
        public const string BorrowFailed = "POOL04";
        public const string BorrowTimeout = "POOL05";

        // Return flow
        public const string ReturnRequested = "POOL11";
        public const string BrowserReturned = "POOL12";
        public const string ReturnFailed = "POOL13";

        // Cleanup/auto-stop
        public const string ScanStarted = "POOL20";
        public const string BrowserReleased = "POOL21";
        public const string ItemAutoStopped = "POOL22";
        public const string CleanupFailed = "POOL23";

        // Pool management
        public const string PoolInitialized = "POOL30";
        public const string CapacityChanged = "POOL31";
        public const string WorkerRegistered = "POOL32";
        public const string WorkerDeregistered = "POOL33";
    }

    /// <summary>
    ///     Launch operations (LCH01-LCH99)
    /// </summary>
    public static class Launch
    {
        // Launch lifecycle
        public const string LaunchCreated = "LCH01";
        public const string LaunchStarted = "LCH02";
        public const string LaunchFinished = "LCH06"; // Moved from LCH03 to avoid conflict with LaunchNotFound
        public const string LaunchFailed = "LCH08"; // Moved from LCH04 to avoid conflict with LaunchCreationFailed
        public const string LaunchForceFinished = "LCH05";

        // Errors/Exceptions
        public const string LaunchNotFound = "LCH03";
        public const string LaunchCreationFailed = "LCH04";
        public const string LaunchAlreadyFinished = "LCH07";

        public const string LaunchOperationFailed = "LCH99";

        // Launch state
        public const string StatusCalculated = "LCH10";
        public const string AggregationsUpdated = "LCH11";
        public const string AutoStopTriggered = "LCH12";

        // Finish launch workflow
        public const string FinishLaunchStarted = "LCH13";
        public const string AuthorizationStarted = "LCH14";
        public const string TerminalStateCheckStarted = "LCH15";
        public const string StatusCalculationStarted = "LCH16";
        public const string StatusCalculationFallback = "LCH17";
        public const string LaunchUpdateStarted = "LCH18";
        public const string LaunchUpdated = "LCH19";
        public const string CacheInvalidationStarted = "LCH20";
        public const string CacheInvalidated = "LCH21";
        public const string CacheInvalidationFailed = "LCH22";

        // Delete launch workflow
        public const string DeleteLaunchStarted = "LCH23";
        public const string DeleteAuthorizationStarted = "LCH24";
        public const string DeleteTransactionStarted = "LCH25";
        public const string LaunchDeleted = "LCH26";
        public const string DeleteLaunchCompleted = "LCH27";

        // Launch data (renumbered)
        public const string AttributesUpdated = "LCH30";
        public const string DescriptionUpdated = "LCH31";
        public const string MetadataUpdated = "LCH32";

        // Force finish launch workflow
        public const string ForceFinishStarted = "LCH40";
        public const string ForceFinishLaunchStatusChecked = "LCH41";
        public const string ActiveTestItemsFound = "LCH42";
        public const string TestItemStopped = "LCH43";
        public const string BrowserReleased = "LCH44";
        public const string ForceFinishLaunchStatusUpdated = "LCH45";
        public const string ForceFinishAggregationsRecalculated = "LCH46";
        public const string ForceFinishAuditLogged = "LCH47";
        public const string ForceFinishCacheInvalidated = "LCH48";
        public const string ForceFinishCompleted = "LCH49";
    }

    /// <summary>
    ///     Log item operations (LOG01-LOG99)
    /// </summary>
    public static class LogItem
    {
        public const string LogItemCreated = "LOG01";
        public const string LogItemCreationFailed = "LOG02";
        public const string LogItemBatchCreated = "LOG03";
        public const string LogItemBatchFailed = "LOG04";
        public const string LogItemRetrieved = "LOG05";
        public const string LogItemRetrievalFailed = "LOG06";
        public const string LogItemsForTestItemRetrieved = "LOG07";
        public const string LogItemsForLaunchRetrieved = "LOG08";

        // Batch processing
        public const string LogItemBatchValidationStarted = "LOG10";
        public const string LogItemBatchValidationComplete = "LOG11";
        public const string LogItemBatchInvalidItemsSkipped = "LOG12";
        public const string TestItemLookupStarted = "LOG13";
        public const string TestItemFound = "LOG14";

        // Event publishing
        public const string LogItemEventPublished = "LOG20";
        public const string LogItemEventPublishFailed = "LOG21";
        public const string LogItemBatchEventsPublished = "LOG22";
        public const string LogItemEventPublishConfirmed = "LOG23";
        public const string BatchProcessingStarted = "LOG24";

        // Query operations
        public const string LogItemQueryExecuted = "LOG30";
        public const string LogItemQueryFailed = "LOG31";
        public const string QueryCompleted = "LOG40";
        public const string QueryForLaunchCompleted = "LOG41";
    }

    /// <summary>
    ///     Test item operations (ITEM01-ITEM99)
    /// </summary>
    public static class TestItem
    {
        // Item lifecycle
        public const string ItemCreated = "ITEM01";
        public const string ItemPersisted = "ITEM02";
        public const string ItemStarted = "ITEM06"; // Moved from ITEM03 to avoid conflict with TestItemNotFound
        public const string ItemFinished = "ITEM07"; // Moved from ITEM04 to avoid conflict with TestItemCreationFailed
        public const string ItemFailed = "ITEM08"; // Moved from ITEM05 to avoid conflict with generic error

        // Errors/Exceptions
        public const string TestItemNotFound = "ITEM03";
        public const string TestItemCreationFailed = "ITEM04";

        public const string TestItemOperationFailed = "ITEM99";

        // Item hierarchy
        public const string ChildAdded = "ITEM10";
        public const string ParentLinked = "ITEM11";
        public const string TreeLoaded = "ITEM12";

        // Item data
        public const string LogAdded = "ITEM20";
        public const string ArtifactUploaded = "ITEM21";
        public const string StatusUpdated = "ITEM22";
        public const string ParametersSet = "ITEM23";
    }

    /// <summary>
    ///     Ingestion service operations (ING01-ING99)
    /// </summary>
    public static class Ingestion
    {
        // Batch processing
        public const string BatchReceived = "ING01";
        public const string BatchStarted = "ING02";
        public const string BatchCompleted = "ING03";
        public const string BatchFailed = "ING04";

        // Database writes
        public const string TestItemsWritten = "ING10";
        public const string LogItemsWritten = "ING11";
        public const string CommandsWritten = "ING12";
        public const string WriteFailed = "ING13";

        // Token optimization
        public const string TokenCacheHit = "ING20";
        public const string TokenCacheMiss = "ING21";
        public const string TokenCreated = "ING22";
    }

    /// <summary>
    ///     Worker node operations (WRK01-WRK99)
    /// </summary>
    public static class Worker
    {
        // Browser lifecycle
        public const string BrowserStartupRequested = "WRK01";
        public const string PlaywrightLaunched = "WRK02";
        public const string BrowserConnected = "WRK03";
        public const string BrowserStartupFailed = "WRK04";
        public const string SidecarReady = "WRK05";
        public const string SidecarExited = "WRK06";
        public const string BrowserBorrowed = "WRK16";
        public const string BrowserReturned = "WRK17";

        // Pool management (internal worker)
        public const string PoolWarmingStarted = "WRK07";
        public const string PoolWarmingCompleted = "WRK08";
        public const string PoolResizeStarted = "WRK18";
        public const string PoolResizeCompleted = "WRK19";
        public const string ReconcileStarted = "WRK09";
        public const string ReconcileCompleted = "WRK10";

        // Browser cleanup
        public const string CleanupRequested = "WRK11";
        public const string BrowserClosed = "WRK12";
        public const string CleanupFailed = "WRK13";
        public const string OrphanCleanupStarted = "WRK14";
        public const string OrphanCleanupCompleted = "WRK15";
        public const string LabelRemoved = "WRK27";
        public const string BrowserPruned = "WRK28";

        // Worker registration
        public const string RegistrationStarted = "WRK20";
        public const string RegistrationSent = "WRK21";
        public const string RegistrationConfirmed = "WRK22";
        public const string RegistrationFailed = "WRK23";
        public const string RegistrationVerificationStarted = "WRK24";
        public const string RegistrationVerificationSucceeded = "WRK25";
        public const string RegistrationVerificationFailed = "WRK26";

        // Health
        public const string HealthCheckStarted = "WRK30";
        public const string HealthCheckCompleted = "WRK31";
        public const string HealthCheckFailed = "WRK32";

        // Heartbeat
        public const string HeartbeatStarted = "WRK40";
        public const string HeartbeatTick = "WRK41";
        public const string HeartbeatFailed = "WRK42";
        public const string HeartbeatGapDetected = "WRK43";
        public const string HeartbeatStopped = "WRK44";
    }

    /// <summary>
    ///     Node management operations (NOD01-NOD99)
    /// </summary>
    public static class Node
    {
        public const string RegistrationReceived = "NOD01";
        public const string RegistrationSuccess = "NOD02";
        public const string RegistrationUpdate = "NOD03";
        public const string RegistrationFailed = "NOD04";
        public const string RegistrationCleanup = "NOD05";
    }

    /// <summary>
    ///     Storage operations (STG01-STG99)
    /// </summary>
    public static class Storage
    {
        // MinIO/S3 operations
        public const string UploadStarted = "STG01";
        public const string UploadCompleted = "STG02";
        public const string UploadFailed = "STG03";

        public const string DownloadStarted = "STG10";
        public const string DownloadCompleted = "STG11";
        public const string DownloadFailed = "STG12";

        public const string DeleteStarted = "STG20";
        public const string DeleteCompleted = "STG21";
        public const string DeleteFailed = "STG22";

        // Pre-signed URLs
        public const string UrlGenerated = "STG30";
        public const string UrlGenerationFailed = "STG31";
    }

    /// <summary>
    ///     Housekeeping service operations (HKEP01-HKEP99)
    /// </summary>
    public static class Housekeeping
    {
        // Launch retention
        public const string LaunchRetentionStarted = "HKEP01";
        public const string LaunchesDeleted = "HKEP02";
        public const string LaunchRetentionCompleted = "HKEP03";

        // Log retention
        public const string LogRetentionStarted = "HKEP10";
        public const string LogItemsDeleted = "HKEP11";
        public const string OrphanedTokensCleaned = "HKEP12";
        public const string LogRetentionCompleted = "HKEP13";

        // Artifact retention
        public const string ArtifactRetentionStarted = "HKEP20";
        public const string ArtifactsDeleted = "HKEP21";
        public const string PhysicalFilesDeleted = "HKEP22";
        public const string ArtifactRetentionCompleted = "HKEP23";

        // Audit retention
        public const string AuditRetentionStarted = "HKEP30";
        public const string AuditEntriesDeleted = "HKEP31";
        public const string AuditRetentionCompleted = "HKEP32";

        // Launch auto-stop
        public const string LaunchAutoStopStarted = "HKEP40";
        public const string LaunchAutoStopped = "HKEP41";
        public const string LaunchAutoStopCompleted = "HKEP42";

        // System
        public const string BootstrapCompleted = "HKEP90";
    }

    /// <summary>
    ///     Redis operations (RDS01-RDS99)
    /// </summary>
    public static class Redis
    {
        // Connection & Lifecycle
        public const string ClientInitialized = "RDS01";
        public const string Connected = "RDS02";
        public const string ConnectionError = "RDS04";

        // Key-Value & Set Operations
        public const string KeyOperationSuccess = "RDS10";
        public const string KeyOperationFailed = "RDS11";
        public const string SetOperationSuccess = "RDS20";
        public const string SetOperationFailed = "RDS21";

        // Transactions & Bulk Operations
        public const string TransactionStarted = "RDS30";
        public const string TransactionCommitted = "RDS31";
        public const string TransactionFailed = "RDS32";

        // Worker Specific
        public const string HeartbeatSent = "RDS50";

        public const string OperationFailed = "RDS99";
    }

    /// <summary>
    ///     Database operations (DB01-DB99)
    /// </summary>
    public static class Database
    {
        // Migration lifecycle
        public const string MigrationStarted = "DB01";
        public const string DatabaseConnectionTested = "DB02";
        public const string DatabaseReady = "DB03";
        public const string MigrationUpgradeCheckStarted = "DB04";
        public const string MigrationUpgradeNotRequired = "DB05";
        public const string MigrationUpgradeRequired = "DB06";
        public const string MigrationScriptsDiscovered = "DB07";

        // Script execution
        public const string MigrationScriptStarting = "DB08";
        public const string MigrationScriptExecuting = "DB09";
        public const string MigrationScriptCompleted = "DB10";

        // Transaction management
        public const string MigrationTransactionStarted = "DB11";
        public const string MigrationTransactionCommitted = "DB12";
        public const string MigrationTransactionRolledBack = "DB13";

        // Verification and completion
        public const string MigrationVerificationStarted = "DB14";
        public const string MigrationVerificationCompleted = "DB15";
        public const string MigrationVersionJournalUpdated = "DB16";
        public const string MigrationCompletedSuccessfully = "DB17";

        // Errors
        public const string MigrationConnectionFailed = "DB18";
        public const string MigrationFailed = "DB19";

        // Connection management
        public const string ConnectionOpened = "DB30";
        public const string ConnectionClosed = "DB31";
        public const string ConnectionPoolExhausted = "DB32";
        public const string ConnectionRetry = "DB33";

        // General transactions
        public const string TransactionStarted = "DB40";
        public const string TransactionCommitted = "DB41";
        public const string TransactionRolledBack = "DB42";
        public const string TransactionFailed = "DB43";
        public const string OperationFailed = "DB99";

        // Legacy aliases (deprecated, use specific migration codes)
        [Obsolete("Use MigrationScriptCompleted instead")]
        public const string MigrationApplied = "DB10"; // Maps to MigrationScriptCompleted
    }

    /// <summary>
    ///     Node Sweeper operations (NSR01-NSR99)
    /// </summary>
    public static class NodeSweeper
    {
        // Leader election
        public const string LeaderElectionStarted = "NSR01";
        public const string LeaderLockAcquired = "NSR02";
        public const string LeaderLockRenewed = "NSR03";
        public const string LeaderLockFailed = "NSR04";

        // Scanning
        public const string ScanningStarted = "NSR10";
        public const string NodesRetrieved = "NSR11";
        public const string NodeProcessingStarted = "NSR12";
        public const string NodeSkippedHealthy = "NSR13";
        public const string NodeExpired = "NSR14";
        public const string NodeQuarantined = "NSR15";
        public const string NodeExpiredRemoved = "NSR16";

        // Pruning
        public const string AvailableEntriesPruned = "NSR20";
        public const string InuseEntriesPruned = "NSR21";

        // Errors
        public const string NodeProcessingFailed = "NSR30";
        public const string ScanningFailed = "NSR31";
        public const string RedisTimeout = "NSR32";

        // Completion
        public const string ScanningCompleted = "NSR40";
        public const string QuarantineGaugeUpdated = "NSR41";
    }

    /// <summary>
    ///     Background Services / Orphan Detection (ORP01-ORP99)
    /// </summary>
    public static class OrphanDetector
    {
        public const string LeaderElectionStarted = "ORP01";
        public const string LeaderLockAcquired = "ORP02";
        public const string LeaderLockRenewed = "ORP03";
        public const string LeaderLockReleased = "ORP04";

        public const string ScanningStarted = "ORP10";
        public const string ScanningHeartbeatExpired = "ORP11";
        public const string ScanningOrphanedPidsFound = "ORP12";
        public const string ScanningComplete = "ORP13";

        public const string PidCleanupStarted = "ORP20";
        public const string PidCleaned = "ORP21"; // per PID
        public const string PidCleanupFailed = "ORP22";
        public const string WorkerKeysCleanupStarted = "ORP23";
        public const string WorkerKeysCleaned = "ORP24";

        public const string DetectFailed = "ORP30";
        public const string LeaderLockFailed = "ORP31";
    }

    /// <summary>
    ///     Event Publisher operations (EVT01-EVT99)
    /// </summary>
    public static class EventPublisher
    {
        // Success events (published successfully)
        public const string TestItemPublished = "EVT01";
        public const string CommandPublished = "EVT02";
        public const string LogItemPublished = "EVT03";
        public const string AuditPublished = "EVT04";
        public const string ArtifactPublished = "EVT05";

        // Batch operations
        public const string BatchPublished = "EVT06";
        public const string BatchPartialFailure = "EVT07";

        // Debug/Tracing
        public const string MessageSizeLogged = "EVT09";

        // Failures
        public const string PublishFailed = "EVT10";
        public const string ConnectionLost = "EVT11";
        public const string RetryAttempt = "EVT12";
        public const string RetryFailed = "EVT13";

        // Infrastructure
        public const string ChannelCreated = "EVT20";
        public const string ChannelClosed = "EVT21";
        public const string ExchangeDeclared = "EVT22";
        public const string QueueDeclared = "EVT23";
        public const string AdditionalQueueDeclared = "EVT24";
        public const string PublishConfirmed = "EVT25";
    }

    /// <summary>
    ///     Borrow TTL Sweeper operations (BRT01-BRT99)
    /// </summary>
    public static class BorrowTtlSweeper
    {
        // Scanning
        public const string ScanStarted = "BRT01";
        public const string ScanCompleted = "BRT02";
        public const string SessionKeyFound = "BRT03";

        // TTL checks
        public const string TtlCheckStarted = "BRT10";
        public const string TtlExpired = "BRT11";
        public const string TtlStillValid = "BRT12";

        // Session metadata
        public const string SessionMetadataLoaded = "BRT20";
        public const string SessionMetadataEmpty = "BRT21";

        // Browser return
        public const string BrowserReturnStarted = "BRT30";
        public const string BrowserReturned = "BRT31";
        public const string BrowserReturnFailed = "BRT32";

        // Notifications
        public const string CommandEventPublished = "BRT40";
        public const string SignalRNotificationSent = "BRT41";
        public const string EventPublished = "BRT42";

        // AutoStop
        public const string RunAutoStopped = "BRT50";

        // Completion
        public const string BatchProcessed = "BRT60";
    }

    /// <summary>
    ///     Browser Auto-Stop operations (BST01-BST99)
    /// </summary>
    public static class BrowserAutoStop
    {
        // Scanning and selection
        public const string ScanStarted = "BST01";
        public const string ActiveItemsRetrieved = "BST02";
        public const string ItemSelected = "BST03";

        // Validation
        public const string InactivityCheckStarted = "BST10";
        public const string InactivityMet = "BST11";
        public const string DurationCheckStarted = "BST12";
        public const string DurationExceeded = "BST13";

        // Outstanding browser detection
        public const string CommandLogAnalysisStarted = "BST20";
        public const string LaunchCommandFound = "BST21";
        public const string ReturnCommandFound = "BST22";
        public const string OutstandingBrowsersDetected = "BST23";

        // Browser release
        public const string BrowserReturnStarted = "BST30";
        public const string BrowserReturned = "BST31";
        public const string BrowserReturnFailed = "BST32";

        // Notification
        public const string SignalRNotificationSent = "BST40";
        public const string EventPublished = "BST41";

        // Completion
        public const string ItemProcessed = "BST50";
        public const string BatchCompleted = "BST51";
    }

    /// <summary>
    ///     Browser Health Checker operations (BHC01-BHC99)
    /// </summary>
    public static class BrowserHealth
    {
        // Loop lifecycle
        public const string LoopStarted = "BHC01";
        public const string LoopCompleted = "BHC02";
        public const string LoopError = "BHC03";

        // Individual check
        public const string CheckStarted = "BHC10";
        public const string CheckPassed = "BHC11";
        public const string CheckFailed = "BHC12";
        public const string CheckException = "BHC13";

        // Remediation
        public const string RecycleTriggered = "BHC20";
        public const string RecycleFailed = "BHC21";
    }

    /// <summary>
    ///     Artifacts operations (ART01-ART99)
    /// </summary>
    public static class Artifacts
    {
        // Upload operations
        public const string ArtifactUploaded = "ART01";
        public const string ArtifactUploadStarted = "ART02";
        public const string ArtifactUploadCompleted = "ART03";
        public const string ArtifactUploadFailed = "ART04";

        // Download operations
        public const string ArtifactDownloaded = "ART10";
        public const string ArtifactDownloadStarted = "ART11";
        public const string ArtifactDownloadCompleted = "ART12";
        public const string ArtifactDownloadFailed = "ART13";

        // List operations
        public const string ArtifactListed = "ART20";
        public const string ArtifactListedBatch = "ART21";

        // URL operations
        public const string ArtifactUrlGenerated = "ART30";

        // Batch operations
        public const string ArtifactBatchZipCreated = "ART40";
        public const string ArtifactBatchZipFailed = "ART41";

        // Storage/caching
        public const string ArtifactCacheHit = "ART50";
        public const string ArtifactCacheMiss = "ART51";
        public const string ArtifactStorageError = "ART60";
    }

    /// <summary>
    ///     Project Settings operations (PRJ01-PRJ99)
    /// </summary>
    public static class ProjectSettings
    {
        // Retrieval operations
        public const string SettingsRetrieved = "PRJ01";
        public const string SettingsMissing = "PRJ02";

        // Update operations
        public const string SettingsUpdated = "PRJ03";
        public const string SettingsValidationStarted = "PRJ04";
        public const string SettingsValidationFailed = "PRJ05";
        public const string SettingsValidationSucceeded = "PRJ06";

        // Retention validations
        public const string RetentionValuesValidated = "PRJ10";
        public const string RetentionValueInvalid = "PRJ11";
        public const string RetentionHierarchyViolation = "PRJ12";

        // Persistence
        public const string SettingsPersisted = "PRJ20";
        public const string SettingsPersistenceFailed = "PRJ21";

        // Configuration loading
        public const string ConfigurationLoaded = "PRJ30";
        public const string ConfigurationMissing = "PRJ31";
    }

    /// <summary>
    ///     Password Reset operations (PWD01-PWD99)
    /// </summary>
    public static class PasswordReset
    {
        // Request lifecycle
        public const string ResetRequested = "PWD01";
        public const string ResetRateLimitExceeded = "PWD02";
        public const string ResetRequestDenied = "PWD03";

        // Token operations
        public const string TokenGenerated = "PWD10";
        public const string TokenValidated = "PWD11";
        public const string TokenExpired = "PWD12";
        public const string TokenInvalid = "PWD13";

        // Password reset completion
        public const string PasswordResetCompleted = "PWD20";
        public const string PasswordResetFailed = "PWD21";

        // Email operations
        public const string EmailSent = "PWD30";
        public const string EmailSendFailed = "PWD31";

        // Rate limiting
        public const string RateLimitIncremented = "PWD40";
    }

    /// <summary>
    ///     System-level operations (SYS01-SYS99)
    /// </summary>
    public static class System
    {
        public const string BootstrapStarted = "SYS01";
        public const string BootstrapCompleted = "SYS02";
        public const string BootstrapFailed = "SYS03";
        public const string BootstrapUserCreated = "SYS10";
        public const string BootstrapProjectCreated = "SYS11";
        public const string BootstrapMembershipCreated = "SYS12";
        public const string BootstrapSettingsInitialized = "SYS13";
    }

    /// <summary>
    ///     Web server operations (WSH01-WSH99)
    /// </summary>
    public static class WebServer
    {
        public const string ServerStarting = "WSH01";
        public const string ServerStarted = "WSH02";
        public const string ServerStopping = "WSH03";
        public const string ServerStopped = "WSH04";
        public const string EndpointsRegistered = "WSH05";
        public const string ConfigurationDumped = "WSH06";
        public const string ListeningAddresses = "WSH07";
        public const string RequestReceived = "WSH08";
        public const string RequestProcessed = "WSH09";
        public const string RequestFailed = "WSH10";
    }
}
