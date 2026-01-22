-- V43: Add commands deletion to delete_old_log_items function
-- Bug: Commands table rows were not being deleted during log retention cleanup
-- Fix: Add commands deletion step between log_items and token cleanup
-- Date: 2025-12-10
-- Builds on: V42 (which fixed project_key filtering via joins through launches)

-- ============================================================================
-- Update delete_old_log_items to include commands deletion
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
    v_commands_deleted       INT := 0;
    v_log_tokens_deleted     INT := 0;
    v_command_tokens_deleted INT := 0;
BEGIN
    -- Step 1: Delete old log_items (join through test_items → launches for project filtering)
    WITH deleted_logs AS (
        DELETE FROM log_items li
            USING test_items ti
                JOIN launches l ON ti.launch_id = l.id
            WHERE li.test_item_uuid = ti.run_id
                AND l.project_key = p_project_key
                AND li.created_at < p_cutoff_date
            RETURNING li.id, li.token_hash)
    SELECT COUNT(*)
    INTO v_log_items_deleted
    FROM deleted_logs;

    -- Step 2: Delete old commands (join through test_items → launches for project filtering)
    -- Commands table doesn't have project_key, so we join with test_items → launches
    WITH deleted_commands AS (
        DELETE FROM commands c
            USING test_items ti
                JOIN launches l ON ti.launch_id = l.id
            WHERE c.run_id = ti.run_id
                AND l.project_key = p_project_key
                AND c.timestamp_utc < p_cutoff_date
            RETURNING c.run_id, c.token_hash)
    SELECT COUNT(*)
    INTO v_commands_deleted
    FROM deleted_commands;

    -- Step 3: Delete orphaned log_tokens (tokens with no remaining log_items)
    WITH orphaned_log_tokens AS (
        DELETE FROM log_tokens lt
            WHERE NOT EXISTS (SELECT 1
                              FROM log_items li
                              WHERE li.token_hash = lt.token_hash)
            RETURNING token_hash)
    SELECT COUNT(*)
    INTO v_log_tokens_deleted
    FROM orphaned_log_tokens;

    -- Step 4: Delete orphaned command_tokens (tokens with no remaining references from commands table)
    WITH orphaned_command_tokens AS (
        DELETE FROM command_tokens ct
            WHERE NOT EXISTS (SELECT 1
                              FROM commands c
                              WHERE c.token_hash = ct.token_hash)
            RETURNING token_hash)
    SELECT COUNT(*)
    INTO v_command_tokens_deleted
    FROM orphaned_command_tokens;

    -- Return JSON result with all 4 counts
    RETURN jsonb_build_object(
            'log_items_deleted', v_log_items_deleted,
            'commands_deleted', v_commands_deleted,
            'log_tokens_deleted', v_log_tokens_deleted,
            'command_tokens_deleted', v_command_tokens_deleted
           );
END;
$$;

COMMENT ON FUNCTION delete_old_log_items IS 'Deletes log items, commands, and orphaned tokens (log_tokens, command_tokens), returns JSONB with 4 counts. V43: Added commands deletion step. V42: Fixed project_key filtering via joins.';
