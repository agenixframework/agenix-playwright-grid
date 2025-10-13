-- ========================================
-- V1__init.sql - Comprehensive Initial Schema
-- ========================================
--
-- This migration consolidates all previous migrations (V1-V22) into a single
-- comprehensive schema with the  model.
--
-- Key Changes from Legacy:
-- - Table 'runs' renamed to 'test_items' ( alignment)
-- - Table 'test_cases' merged into 'test_items'
-- - Added fields: item_type, has_stats, parent_item_id
-- - Simplified test_artifacts FK structure
-- - Comprehensive indexes and constraints
--
-- Schema Version: V1 (Model)
-- Created: 2025-01-24
-- Migration Tool: Evolve
-- ========================================

-- ========================================
-- SECTION 1: CORE TEST RESULTS TABLES
-- ========================================

-- ----------------------------------------
-- Table: launches
-- Represents a test execution session (e.g., CI build, manual test run)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS launches
(
    -- Identity
    id
    UUID
    PRIMARY
    KEY
    DEFAULT
    gen_random_uuid
(
),
    db_id BIGSERIAL NOT NULL UNIQUE, -- Globally unique sequential ID for URLs

-- Metadata
    name TEXT NOT NULL,
    description TEXT NULL,
    attributes TEXT[] NOT NULL DEFAULT '{}',

    -- Ownership & Project
    owner_api_key TEXT NOT NULL,
    owner_username TEXT NULL,
    project_key TEXT NOT NULL,

    -- Status & Lifecycle
    status TEXT NOT NULL DEFAULT 'InProgress'
    CHECK
(
    status
    IN
(
    'InProgress',
    'Finished',
    'Stopped',
    'Failed'
)),
    start_time TIMESTAMPTZ NOT NULL,
    finish_time TIMESTAMPTZ NULL,
    last_activity TIMESTAMPTZ NULL,

    -- Launch Number (sequential per project and name)
    launch_number INT NOT NULL,

    -- Test Run Aggregations
    total_test_runs INT NOT NULL DEFAULT 0,
    finished_test_runs INT NOT NULL DEFAULT 0,
    running_test_runs INT NOT NULL DEFAULT 0,
    stopped_test_runs INT NOT NULL DEFAULT 0,
    errored_test_runs INT NOT NULL DEFAULT 0,

    -- Test Result Aggregations (from V19)
    total_tests INTEGER DEFAULT 0,
    passed_tests INTEGER DEFAULT 0,
    failed_tests INTEGER DEFAULT 0,
    skipped_tests INTEGER DEFAULT 0,
    timedout_tests INTEGER DEFAULT 0,

    -- Flags (from V7)
    is_important BOOLEAN NOT NULL DEFAULT FALSE,
    retention_override_days INT NULL,
    display_on_launches BOOLEAN NOT NULL DEFAULT TRUE
    );

-- Launch Indexes
CREATE INDEX ix_launches_db_id ON launches (db_id);
CREATE INDEX ix_launches_project_key ON launches (project_key);
CREATE INDEX ix_launches_project_key_start_time ON launches (project_key, start_time DESC);
CREATE INDEX ix_launches_project_key_name ON launches (project_key, name);
CREATE INDEX ix_launches_owner_api_key ON launches (owner_api_key);
CREATE INDEX ix_launches_start_time ON launches (start_time DESC);
CREATE INDEX ix_launches_status ON launches (status);
CREATE INDEX ix_launches_last_activity ON launches (last_activity) WHERE status = 'InProgress';
CREATE INDEX ix_launches_project_status ON launches (project_key, status);
CREATE INDEX idx_launches_test_aggregations ON launches (total_tests, passed_tests, failed_tests);

-- Launch Comments
COMMENT
ON TABLE launches IS 'Test execution sessions grouping multiple suites and test items';
COMMENT
ON COLUMN launches.status IS 'Launch lifecycle status: InProgress|Finished|Stopped|Failed';
COMMENT
ON COLUMN launches.last_activity IS 'Timestamp of last activity, used for inactivity timeout detection';
COMMENT
ON COLUMN launches.total_tests IS 'Total number of test cases across all test runs in this launch';
COMMENT
ON COLUMN launches.is_important IS 'Flag indicating this launch should be retained longer';

-- ----------------------------------------
-- ========================================
-- NOTE: Suites table removed - suites are now stored in test_items table
-- with item_type = 'Suite'. This provides a unified hierarchical model.
-- ========================================

-- ----------------------------------------
-- Table: test_items (NEW NAME - was 'runs')
-- Represents test items in the hierarchy:
-- - Test/Scenario (borrows browser)
-- - Step (test action)
-- - Hook (before/after setup/teardown)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS test_items
(
    -- Primary Key (keeping 'run_id' name for backward compatibility)
    run_id
    UUID
    PRIMARY
    KEY
    DEFAULT
    gen_random_uuid
(
),

    --  Hierarchy
    launch_id UUID NOT NULL REFERENCES launches
(
    id
) ON DELETE CASCADE,
    parent_item_id UUID NULL REFERENCES test_items
(
    run_id
)
  ON DELETE CASCADE,

    --  Item Metadata
    item_type TEXT NOT NULL DEFAULT 'Test'
    CHECK
(
    item_type
    IN
(
    'Suite',
    'Story',
    'Test',
    'Scenario',
    'Step',
    'BeforeSuite',
    'BeforeClass',
    'BeforeMethod',
    'BeforeTest',
    'AfterSuite',
    'AfterClass',
    'AfterMethod',
    'AfterTest'
)),
    has_stats BOOLEAN NOT NULL DEFAULT TRUE,
    name TEXT NOT NULL,
    description TEXT NULL,
    attributes TEXT[] NOT NULL DEFAULT '{}',

    -- Timestamps
    start_time TIMESTAMPTZ NOT NULL,
    finish_time TIMESTAMPTZ NULL,

    -- Browser Session Metadata (from V16 - for Test/Scenario items)
    browser_id TEXT NULL,
    websocket_endpoint TEXT NULL,
    browser_type TEXT NULL,
    worker_node_id TEXT NULL,
    playwright_version TEXT NULL,
    browser_version TEXT NULL,

    -- Status: Separated Concerns (from V21)
    -- session_status: Browser/infrastructure lifecycle
    session_status TEXT NOT NULL DEFAULT 'Queued'
    CHECK
(
    session_status
    IN
(
    'Queued',
    'Running',
    'Completed',
    'Stopped',
    'AutoStopped',
    'Aborted'
)),
    -- computed_status: Test execution outcome
    computed_status TEXT NULL
    CHECK
(
    computed_status
    IS
    NULL
    OR
    computed_status
    IN
(
    'InProgress',
    'Passed',
    'Failed',
    'Skipped',
    'Timedout',
    'Cancelled',
    'Errored'
)),
    -- status: DEPRECATED - legacy field for backward compatibility
    status TEXT NULL,

    -- Test Case Details (merged from test_cases table - V18)
    test_title TEXT NULL,
    test_file TEXT NULL,
    line_number INTEGER NULL,
    error_message TEXT NULL,
    error_stack TEXT NULL,
    steps_json JSONB NULL,
    stdout_json JSONB NULL,
    stderr_json JSONB NULL,
    retry_attempt INTEGER NULL,
    tags TEXT[] NULL,

    --  Additional Fields
    code_ref TEXT NULL, -- e.g., "tests/auth/login.spec.ts:42"
    parameters JSONB NULL, -- Parameterized test parameters
    unique_id TEXT NULL, -- Framework-specific unique identifier

-- Test Result Aggregations (from V19 - for container items like Scenario with Steps)
    total_tests INTEGER DEFAULT 0,
    passed_tests INTEGER DEFAULT 0,
    failed_tests INTEGER DEFAULT 0,
    skipped_tests INTEGER DEFAULT 0,
    timedout_tests INTEGER DEFAULT 0,

    -- Metadata
    created_at TIMESTAMPTZ DEFAULT NOW
(
),
    updated_at TIMESTAMPTZ DEFAULT NOW
(
),

    -- Legacy/Compatibility Fields (from V1)
    run_json TEXT NULL, -- Legacy JSON storage
    app TEXT NULL, -- Legacy app filter
    browser TEXT NULL, -- Legacy browser filter
    env TEXT NULL, -- Legacy env filter
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    expires_at TIMESTAMPTZ NULL,

    -- Owner tracking (from V4)
    owner_username TEXT NULL
    );

-- Test Items Indexes
-- Hierarchy
CREATE INDEX ix_test_items_launch_id ON test_items (launch_id);
CREATE INDEX ix_test_items_parent_item_id ON test_items (parent_item_id) WHERE parent_item_id IS NOT NULL;
CREATE INDEX ix_runs_launch_status ON test_items (launch_id, status) WHERE launch_id IS NOT NULL;

--  Queries
CREATE INDEX ix_test_items_item_type ON test_items (item_type);
CREATE INDEX ix_test_items_parent_type ON test_items (parent_item_id, item_type);
CREATE INDEX ix_test_items_launch_type ON test_items (launch_id, item_type);
CREATE INDEX ix_test_items_has_stats ON test_items (has_stats) WHERE has_stats = TRUE;

-- Status & Performance
CREATE INDEX ix_test_items_session_status ON test_items (session_status);
CREATE INDEX idx_runs_session_status ON test_items (session_status);
CREATE INDEX ix_test_items_computed_status ON test_items (computed_status);
CREATE INDEX idx_runs_computed_status ON test_items (computed_status);
CREATE INDEX ix_test_items_start_time ON test_items (start_time DESC);
CREATE INDEX ix_runs_by_start ON test_items (start_time DESC);
CREATE INDEX ix_test_items_browser_id ON test_items (browser_id) WHERE browser_id IS NOT NULL;

-- Full-text & Arrays
CREATE INDEX ix_test_items_attributes ON test_items USING GIN(attributes);
CREATE INDEX ix_runs_attributes ON test_items USING GIN(attributes);
CREATE INDEX ix_test_items_tags ON test_items USING GIN(tags) WHERE tags IS NOT NULL;
CREATE INDEX ix_test_items_steps_json ON test_items USING GIN(steps_json) WHERE steps_json IS NOT NULL;
CREATE INDEX ix_test_items_parameters ON test_items USING GIN(parameters) WHERE parameters IS NOT NULL;

-- Legacy
CREATE INDEX ix_test_items_expires ON test_items (expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX ix_runs_expires ON test_items (expires_at);
CREATE INDEX idx_runs_test_aggregations ON test_items (total_tests, passed_tests, failed_tests);

-- Test Items Comments
COMMENT
ON TABLE test_items IS 'test items: tests, scenarios, steps, and hooks with browser lifecycle integration';
COMMENT
ON COLUMN test_items.run_id IS 'Primary key (named run_id for backward compatibility, will be renamed to id in future)';
COMMENT
ON COLUMN test_items.item_type IS 'item type: Test (default), Scenario, Step, Suite, Story, or Before*/After* hooks';
COMMENT
ON COLUMN test_items.has_stats IS 'Whether this item contributes to statistics (false for nested steps)';
COMMENT
ON COLUMN test_items.parent_item_id IS 'Parent test item UUID for nested test structures (e.g., steps within a test)';
COMMENT
ON COLUMN test_items.session_status IS 'Browser session lifecycle status (infrastructure state): Queued|Running|Completed|Stopped|AutoStopped|Aborted';
COMMENT
ON COLUMN test_items.computed_status IS 'Test outcome status: InProgress|Passed|Failed|Skipped|Timedout|Cancelled|Errored';
COMMENT
ON COLUMN test_items.status IS 'DEPRECATED: Legacy status field. Use session_status for browser lifecycle or computed_status for test outcomes';
COMMENT
ON COLUMN test_items.code_ref IS 'Code reference (e.g., "tests/auth/login.spec.ts:42")';
COMMENT
ON COLUMN test_items.parameters IS 'Parameterized test parameters as JSON';
COMMENT
ON COLUMN test_items.unique_id IS 'Framework-specific unique identifier';

-- ----------------------------------------
-- Table: test_artifacts
-- Stores metadata about test artifacts (screenshots, traces, videos, logs)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS test_artifacts
(
    -- Primary key
    id
    UUID
    PRIMARY
    KEY
    DEFAULT
    gen_random_uuid
(
),

    -- Relationship (simplified from V18 - single FK to test_items)
    test_item_id UUID NOT NULL REFERENCES test_items
(
    run_id
) ON DELETE CASCADE,

    -- Artifact details
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    file_size BIGINT NOT NULL,
    storage_path TEXT NOT NULL UNIQUE,
    description TEXT,

    -- Metadata
    uploaded_at TIMESTAMPTZ DEFAULT NOW
(
),
    expires_at TIMESTAMPTZ
    );

-- Test Artifacts Indexes
CREATE INDEX ix_test_artifacts_test_item_id ON test_artifacts (test_item_id);
CREATE INDEX idx_test_artifacts_run_test ON test_artifacts (test_item_id);
CREATE INDEX ix_test_artifacts_storage_path ON test_artifacts (storage_path);
CREATE INDEX idx_test_artifacts_storage_path ON test_artifacts (storage_path);
CREATE INDEX ix_test_artifacts_uploaded_at ON test_artifacts (uploaded_at DESC);
CREATE INDEX idx_test_artifacts_uploaded_at ON test_artifacts (uploaded_at DESC);
CREATE INDEX ix_test_artifacts_expires_at ON test_artifacts (expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX idx_test_artifacts_expires_at ON test_artifacts (expires_at) WHERE expires_at IS NOT NULL;

-- Test Artifacts Comments
COMMENT
ON TABLE test_artifacts IS 'Stores metadata for test artifacts (screenshots, traces, videos, logs)';
COMMENT
ON COLUMN test_artifacts.test_item_id IS 'Reference to test_items table (was run_id,test_id composite key in V18)';
COMMENT
ON COLUMN test_artifacts.storage_path IS 'File path in local storage or blob storage URI';
COMMENT
ON COLUMN test_artifacts.expires_at IS 'TTL timestamp for automatic cleanup';

-- ========================================
-- SECTION 2: ADMIN & AUTHENTICATION TABLES
-- ========================================

-- ----------------------------------------
-- Table: admin_projects
-- Durable mirror of projects for admin/dashboard use
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS admin_projects
(
    -- Identity
    key
    TEXT
    PRIMARY
    KEY,

    -- Metadata
    name
    TEXT
    NOT
    NULL,
    owner_user_id
    TEXT
    NULL,
    status
    INT
    NOT
    NULL,

    -- Statistics
    members_count
    INT
    NOT
    NULL
    DEFAULT
    0,
    runs_count
    INT
    NOT
    NULL
    DEFAULT
    0,
    last_activity_utc
    TIMESTAMPTZ
    NULL,

    -- Audit
    created_utc
    TIMESTAMPTZ
    NOT
    NULL,
    updated_utc
    TIMESTAMPTZ
    NOT
    NULL,
    created_by
    TEXT
    NULL,
    updated_by
    TEXT
    NULL,

    -- Generated column for case-insensitive search
    name_lower
    TEXT
    GENERATED
    ALWAYS AS (
    LOWER
(
    name
)) STORED
    );

-- Admin Projects Indexes
CREATE UNIQUE INDEX ux_admin_projects_name_lower ON admin_projects (name_lower);

-- ----------------------------------------
-- Table: admin_users
-- Durable mirror of users for admin/dashboard use
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS admin_users
(
    -- Identity
    id
    TEXT
    PRIMARY
    KEY,

    -- User Info
    username
    TEXT
    NOT
    NULL,
    email
    TEXT
    NULL,
    role
    INT
    NOT
    NULL,
    status
    INT
    NOT
    NULL,

    -- Statistics
    projects_count
    INT
    NOT
    NULL
    DEFAULT
    0,
    last_login_utc
    TIMESTAMPTZ
    NULL,

    -- Audit
    created_utc
    TIMESTAMPTZ
    NOT
    NULL,
    updated_utc
    TIMESTAMPTZ
    NOT
    NULL,
    created_by
    TEXT
    NULL,
    updated_by
    TEXT
    NULL,

    -- Generated column for case-insensitive email search
    email_lower
    TEXT
    GENERATED
    ALWAYS AS (
    LOWER
(
    email
)) STORED
    );

-- Admin Users Indexes
CREATE UNIQUE INDEX ux_admin_users_email_lower ON admin_users (email_lower) WHERE email_lower IS NOT NULL;

-- ----------------------------------------
-- Table: admin_memberships
-- Project membership tracking
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS admin_memberships
(
    -- Composite key
    project_key
    TEXT
    NOT
    NULL,
    user_id
    TEXT
    NOT
    NULL,

    -- Membership Info
    role
    INT
    NOT
    NULL,

    -- Audit
    created_utc
    TIMESTAMPTZ
    NOT
    NULL,
    updated_utc
    TIMESTAMPTZ
    NOT
    NULL,
    created_by
    TEXT
    NULL,
    updated_by
    TEXT
    NULL,

    PRIMARY
    KEY
(
    project_key,
    user_id
)
    );

-- Admin Memberships Indexes
CREATE INDEX ix_admin_memberships_by_user ON admin_memberships (user_id);

-- ----------------------------------------
-- Table: admin_settings
-- Key-value settings storage (from V6)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS admin_settings
(
    key
    TEXT
    PRIMARY
    KEY,
    value
    TEXT
    NOT
    NULL,
    description
    TEXT
    NULL,
    updated_at
    TIMESTAMPTZ
    NOT
    NULL
    DEFAULT
    NOW
(
),
    updated_by TEXT NULL
    );

-- Admin Settings Comments
COMMENT
ON TABLE admin_settings IS 'Global application settings (key-value store)';

-- ----------------------------------------
-- Table: remember_me_tokens
-- Persistent authentication tokens (from V9)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS remember_me_tokens
(
    id
    SERIAL
    PRIMARY
    KEY,
    user_id
    TEXT
    NOT
    NULL
    REFERENCES
    admin_users
(
    id
) ON DELETE CASCADE,
    token_hash VARCHAR
(
    255
) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW
(
),
    expires_at TIMESTAMPTZ NOT NULL,
    last_used_at TIMESTAMPTZ,
    CONSTRAINT fk_remember_me_user FOREIGN KEY
(
    user_id
) REFERENCES admin_users
(
    id
)
  ON DELETE CASCADE
    );

-- Remember Me Tokens Indexes
CREATE INDEX idx_remember_me_token_hash ON remember_me_tokens (token_hash);
CREATE INDEX idx_remember_me_user_expires ON remember_me_tokens (user_id, expires_at);

-- ========================================
-- SECTION 3: FILTER & PREFERENCES TABLES
-- ========================================

-- ----------------------------------------
-- Table: launch_filters
-- Saved filter configurations for launches page (from V5)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS launch_filters
(
    -- Identity
    id
    UUID
    PRIMARY
    KEY
    DEFAULT
    gen_random_uuid
(
),

    -- Filter Info
    name TEXT NOT NULL,
    description TEXT NULL,
    project_key TEXT NOT NULL,
    user_id TEXT NOT NULL,
    criteria_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    sort_by TEXT NOT NULL DEFAULT 'start_time',
    is_shared BOOLEAN NOT NULL DEFAULT FALSE,

    -- Display preference (from V8)
    display_on_launches BOOLEAN NOT NULL DEFAULT TRUE,

    -- Timestamps
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW
(
),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW
(
)
    );

-- Launch Filters Indexes
CREATE INDEX ix_launch_filters_project_user ON launch_filters (project_key, user_id);
CREATE INDEX ix_launch_filters_user ON launch_filters (user_id);
CREATE INDEX ix_launch_filters_project_shared ON launch_filters (project_key, is_shared) WHERE is_shared = TRUE;
CREATE INDEX ix_launch_filters_created_at ON launch_filters (created_at DESC);

-- ----------------------------------------
-- Table: user_filter_preferences
-- Tracks selected filter per user/project (from V5, V10)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS user_filter_preferences
(
    -- Composite key
    user_id
    TEXT
    NOT
    NULL,
    project_key
    TEXT
    NOT
    NULL,

    -- Preference
    selected_filter_id
    UUID
    NULL
    REFERENCES
    launch_filters
(
    id
) ON DELETE SET NULL,

    -- Display preferences (from V10)
    display_preferences JSONB NULL,

    -- Timestamp
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW
(
),
    PRIMARY KEY
(
    user_id,
    project_key
)
    );

-- User Filter Preferences Indexes
CREATE INDEX ix_user_filter_preferences_project ON user_filter_preferences (project_key);

-- ========================================
-- SECTION 4: FUNCTIONS & TRIGGERS
-- ========================================

-- ----------------------------------------
-- Function: notify_test_case_update
-- Notifies SignalR hub about test item updates (adapted from V18)
-- ----------------------------------------
CREATE
OR REPLACE FUNCTION notify_test_case_update()
RETURNS TRIGGER AS $$
BEGIN
    -- Notify SignalR hub about test item update
    PERFORM
pg_notify(
        'test_case_update',
        json_build_object(
            'runId', NEW.run_id,
            'testId', NEW.run_id,
            'status', NEW.computed_status
        )::text
    );
RETURN NEW;
END;
$$
LANGUAGE plpgsql;

COMMENT
ON FUNCTION notify_test_case_update IS 'Notifies SignalR hub about test item updates via pg_notify';

-- Trigger on test_items insert/update
DROP TRIGGER IF EXISTS trigger_test_case_update ON test_items;
CREATE TRIGGER trigger_test_case_update
    AFTER INSERT OR
UPDATE ON test_items
    FOR EACH ROW
    EXECUTE FUNCTION notify_test_case_update();

-- ----------------------------------------
-- Function: update_test_aggregations
-- Updates test result aggregations (adapted from V19)
-- ----------------------------------------
CREATE
OR REPLACE FUNCTION update_test_aggregations()
RETURNS TRIGGER AS $$
BEGIN
    -- Update parent item aggregations (if parent_item_id exists)
    -- Use single query with aggregation instead of 5 separate subqueries
    IF
NEW.parent_item_id IS NOT NULL THEN
UPDATE test_items s
SET total_tests    = agg.total,
    passed_tests   = agg.passed,
    failed_tests   = agg.failed,
    skipped_tests  = agg.skipped,
    timedout_tests = agg.timedout FROM (
            SELECT
                COALESCE(SUM(r.total_tests), 0) AS total,
                COALESCE(SUM(r.passed_tests), 0) AS passed,
                COALESCE(SUM(r.failed_tests), 0) AS failed,
                COALESCE(SUM(r.skipped_tests), 0) AS skipped,
                COALESCE(SUM(r.timedout_tests), 0) AS timedout
            FROM test_items r
            WHERE r.parent_item_id = NEW.parent_item_id
        ) agg
WHERE s.run_id = NEW.parent_item_id;
END IF;

    -- Update launch aggregations (aggregate from all Suite items in the launch)
    -- Use single query with aggregation instead of 5 separate subqueries
    IF
NEW.launch_id IS NOT NULL THEN
UPDATE launches l
SET total_tests    = agg.total,
    passed_tests   = agg.passed,
    failed_tests   = agg.failed,
    skipped_tests  = agg.skipped,
    timedout_tests = agg.timedout FROM (
            SELECT
                COALESCE(SUM(ti.total_tests), 0) AS total,
                COALESCE(SUM(ti.passed_tests), 0) AS passed,
                COALESCE(SUM(ti.failed_tests), 0) AS failed,
                COALESCE(SUM(ti.skipped_tests), 0) AS skipped,
                COALESCE(SUM(ti.timedout_tests), 0) AS timedout
            FROM test_items ti
            WHERE ti.launch_id = NEW.launch_id AND ti.item_type = 'Suite'
        ) agg
WHERE l.id = NEW.launch_id;
END IF;

RETURN NEW;
END;
$$
LANGUAGE plpgsql;

COMMENT
ON FUNCTION update_test_aggregations IS 'Automatically updates test result aggregations for suites and launches when test items are updated';

-- Trigger to automatically update aggregations
DROP TRIGGER IF EXISTS trigger_test_case_aggregation_update ON test_items;
CREATE TRIGGER trigger_test_case_aggregation_update
    AFTER INSERT OR
UPDATE ON test_items
    FOR EACH ROW
    EXECUTE FUNCTION update_test_aggregations();

-- ----------------------------------------
-- Function: cleanup_expired_test_data
-- Deletes expired test items and artifacts (adapted from V18)
-- ----------------------------------------
CREATE
OR REPLACE FUNCTION cleanup_expired_test_data()
RETURNS INTEGER AS $$
DECLARE
deleted_count INTEGER;
BEGIN
    -- Delete expired test items (CASCADE will delete artifacts)
DELETE
FROM test_items
WHERE expires_at IS NOT NULL
  AND expires_at < NOW();

GET DIAGNOSTICS deleted_count = ROW_COUNT;

RETURN deleted_count;
END;
$$
LANGUAGE plpgsql;

COMMENT
ON FUNCTION cleanup_expired_test_data IS 'Deletes expired test items and their artifacts based on TTL';

-- ----------------------------------------
-- Function: update_test_item_session_status
-- Updates browser session lifecycle status (adapted from V21)
-- ----------------------------------------
CREATE
OR REPLACE FUNCTION update_test_item_session_status(
    p_run_id UUID,
    p_session_status TEXT
) RETURNS void AS $$
BEGIN
    -- Validate session status value
    IF
p_session_status NOT IN ('Queued', 'Running', 'Completed', 'Stopped', 'AutoStopped', 'Aborted') THEN
        RAISE EXCEPTION 'Invalid session_status: %. Must be one of: Queued, Running, Completed, Stopped, AutoStopped, Aborted', p_session_status;
END IF;

    -- Update session status
UPDATE test_items
SET session_status = p_session_status,
    status         = p_session_status, -- Keep legacy field in sync
    updated_at     = NOW()
WHERE run_id = p_run_id;
END;
$$
LANGUAGE plpgsql;

COMMENT
ON FUNCTION update_test_item_session_status IS 'Updates the browser session lifecycle status for a test item. Keeps legacy status field in sync for backward compatibility.';

-- ========================================
-- SECTION 5: COMMAND LOG TABLE
-- ========================================

-- ----------------------------------------
-- Table: commands
-- Command event log for browser operations (borrow, return, etc.)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS commands
(
    run_id
    UUID
    NOT
    NULL,
    timestamp_utc
    TIMESTAMPTZ
    NOT
    NULL,
    kind
    TEXT
    NULL,
    message
    TEXT
    NULL,
    props_json
    TEXT
    NULL,
    test_id
    TEXT
    NULL,
    expires_at
    TIMESTAMPTZ
    NULL,

    PRIMARY
    KEY
(
    run_id,
    timestamp_utc
)
    );

-- Commands Indexes
CREATE INDEX IF NOT EXISTS idx_commands_run_id ON commands(run_id);
CREATE INDEX IF NOT EXISTS idx_commands_expires ON commands(expires_at) WHERE expires_at IS NOT NULL;

-- Commands Comments
COMMENT
ON TABLE commands IS 'Command event log for tracking browser borrow/return operations';
COMMENT
ON COLUMN commands.run_id IS 'Test item ID this command is associated with';
COMMENT
ON COLUMN commands.timestamp_utc IS 'When the command event occurred';
COMMENT
ON COLUMN commands.kind IS 'Command type (ServerLaunch, Return, AutoReturn, etc.)';
COMMENT
ON COLUMN commands.message IS 'Human-readable event description';
COMMENT
ON COLUMN commands.props_json IS 'Additional properties as JSON';
COMMENT
ON COLUMN commands.test_id IS 'Test identifier if applicable';
COMMENT
ON COLUMN commands.expires_at IS 'When this log entry should be cleaned up (TTL)';

-- ========================================
-- SCHEMA INITIALIZATION COMPLETE
-- ========================================
-- This completes the V1 initial schema with:
-- - test_items table (model)
-- - All supporting tables (launches, suites, artifacts, admin, filters)
-- - All indexes optimized for performance
-- - All functions and triggers for automation
-- - Comprehensive documentation via comments
--
-- Next migrations (V2+) will be incremental enhancements
-- ========================================
