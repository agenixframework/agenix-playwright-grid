-- V39: Retention cleanup functions for housekeeping service
-- Author: Claude AI
-- Date: 2025-12-06

-- ============================================================================
-- 1. Launch Retention Cleanup
-- ============================================================================
-- Deletes complete launches with ALL descendants via CASCADE:
-- - test_items (including suites, tests, steps)
-- - log_items
-- - test_artifacts
-- - Any other tables with FK to launches
-- Returns: Number of launches deleted
-- ============================================================================
CREATE OR REPLACE FUNCTION delete_old_launches(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
    RETURNS INT
    LANGUAGE plpgsql
AS
$$
DECLARE
    v_deleted_count INT;
BEGIN
    WITH deleted AS (
        DELETE FROM launches
            WHERE project_key = p_project_key
                AND finish_time IS NOT NULL
                AND finish_time < p_cutoff_date
            RETURNING id)
    SELECT COUNT(*)
    INTO v_deleted_count
    FROM deleted;

    RETURN v_deleted_count;
END;
$$;

COMMENT ON FUNCTION delete_old_launches IS 'Deletes launches older than cutoff date with CASCADE to all descendants';

-- ============================================================================
-- 2. Log Items Retention Cleanup (With Token Cleanup)
-- ============================================================================
-- Deletes log items + orphaned tokens:
-- - log_items rows older than cutoff
-- - Orphaned log_tokens (tokens with NO remaining log_items references)
-- - Orphaned command_tokens (tokens with NO remaining references)
-- Returns: JSONB with 3 counts
-- ============================================================================
CREATE OR REPLACE FUNCTION delete_old_log_items(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
    RETURNS JSONB
    LANGUAGE plpgsql
AS
$$
DECLARE
    v_log_items_deleted      INT := 0;
    v_log_tokens_deleted     INT := 0;
    v_command_tokens_deleted INT := 0;
BEGIN
    -- Step 1: Delete old log_items
    WITH deleted_logs AS (
        DELETE FROM log_items
            WHERE project_key = p_project_key
                AND created_at < p_cutoff_date
            RETURNING id, token_hash)
    SELECT COUNT(*)
    INTO v_log_items_deleted
    FROM deleted_logs;

    -- Step 2: Delete orphaned log_tokens (tokens with no remaining log_items)
    WITH orphaned_log_tokens AS (
        DELETE FROM log_tokens lt
            WHERE NOT EXISTS (SELECT 1
                              FROM log_items li
                              WHERE li.token_hash = lt.token_hash)
            RETURNING token_hash)
    SELECT COUNT(*)
    INTO v_log_tokens_deleted
    FROM orphaned_log_tokens;

    -- Step 3: Delete orphaned command_tokens (tokens with no remaining references)
    -- Note: Assumes commands table has token_hash FK to command_tokens
    WITH orphaned_command_tokens AS (
        DELETE FROM command_tokens ct
            WHERE NOT EXISTS (SELECT 1
                              FROM commands c
                              WHERE c.token_hash = ct.token_hash)
            RETURNING token_hash)
    SELECT COUNT(*)
    INTO v_command_tokens_deleted
    FROM orphaned_command_tokens;

    -- Return JSON result with all 3 counts
    RETURN jsonb_build_object(
            'log_items_deleted', v_log_items_deleted,
            'log_tokens_deleted', v_log_tokens_deleted,
            'command_tokens_deleted', v_command_tokens_deleted
           );
END;
$$;

COMMENT ON FUNCTION delete_old_log_items IS 'Deletes log items and orphaned tokens (log_tokens, command_tokens), returns JSONB with counts';

-- ============================================================================
-- 3. Attachments Retention Cleanup (Hard Delete with Physical Files)
-- ============================================================================
-- Deletes test_artifacts from database (HARD DELETE) and returns artifact
-- details for physical file deletion (MinIO or local filesystem).
-- Returns: JSONB array of deleted artifacts with file details
-- ============================================================================
CREATE OR REPLACE FUNCTION delete_old_attachments(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
    RETURNS JSONB
    LANGUAGE plpgsql
AS
$$
DECLARE
    v_result JSONB;
BEGIN
    -- Delete artifacts and return details for physical file deletion
    WITH deleted_artifacts AS (
        DELETE FROM test_artifacts ta
            USING test_items ti
            WHERE ta.test_item_uuid = ti.run_id
                AND ti.project_key = p_project_key
                AND ta.created_at < p_cutoff_date
            RETURNING
                ta.id,
                ta.storage_path,
                ta.file_name,
                ta.file_size)
    SELECT jsonb_agg(
                   jsonb_build_object(
                           'id', id,
                           'storage_path', storage_path,
                           'file_name', file_name,
                           'file_size', file_size
                   )
           )
    INTO v_result
    FROM deleted_artifacts;

    -- Return empty array if no artifacts deleted
    RETURN COALESCE(v_result, '[]'::jsonb);
END;
$$;

COMMENT ON FUNCTION delete_old_attachments IS 'Hard deletes test_artifacts from database and returns JSONB array with file details for physical deletion';

-- ============================================================================
-- 4. Audit Entries Retention Cleanup
-- ============================================================================
-- Deletes audit_entries older than cutoff date for a specific project.
-- Returns: Number of audit entries deleted
-- ============================================================================
CREATE OR REPLACE FUNCTION delete_old_audit_entries(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
    RETURNS INT
    LANGUAGE plpgsql
AS
$$
DECLARE
    v_deleted_count INT;
BEGIN
    WITH deleted AS (
        DELETE FROM audit_entries
            WHERE project_key = p_project_key
                AND created_at < p_cutoff_date
            RETURNING id)
    SELECT COUNT(*)
    INTO v_deleted_count
    FROM deleted;

    RETURN v_deleted_count;
END;
$$;

COMMENT ON FUNCTION delete_old_audit_entries IS 'Deletes audit entries older than cutoff date for a specific project';

-- ============================================================================
-- End of V39 Migration
-- ============================================================================
