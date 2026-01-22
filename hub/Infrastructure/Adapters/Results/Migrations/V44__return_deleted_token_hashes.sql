-- V44: Return deleted token hashes for Redis cleanup
-- Purpose: Enable Redis cleanup by returning which tokens were deleted from PostgreSQL
-- Date: 2025-12-17
-- Builds on: V43 (which added commands deletion to log retention)

-- ============================================================================
-- Modify delete_old_log_items to return deleted token hashes
-- ============================================================================
CREATE
OR REPLACE FUNCTION delete_old_log_items(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
v_log_items_deleted INT := 0;
    v_commands_deleted
INT := 0;
    v_log_tokens_deleted
INT := 0;
    v_command_tokens_deleted
INT := 0;
    v_deleted_log_tokens
TEXT[] := '{}';
    v_deleted_command_tokens
TEXT[] := '{}';
BEGIN
    -- Step 1: Delete old log_items (join through test_items → launches for project filtering)
WITH deleted_logs AS (
DELETE
FROM log_items li USING test_items ti
        JOIN launches l
ON ti.launch_id = l.id
WHERE li.test_item_uuid = ti.run_id
  AND l.project_key = p_project_key
  AND li.created_at
    < p_cutoff_date
    RETURNING li.id
    , li.token_hash
    )
SELECT COUNT(*)
INTO v_log_items_deleted
FROM deleted_logs;

-- Step 2: Delete old commands (join through test_items → launches for project filtering)
WITH deleted_commands AS (
DELETE
FROM commands c USING test_items ti
        JOIN launches l
ON ti.launch_id = l.id
WHERE c.run_id = ti.run_id
  AND l.project_key = p_project_key
  AND c.timestamp_utc
    < p_cutoff_date
    RETURNING c.run_id
    , c.token_hash
    )
SELECT COUNT(*)
INTO v_commands_deleted
FROM deleted_commands;

-- Step 3: Delete orphaned log_tokens and CAPTURE the deleted hashes
WITH orphaned_log_tokens AS (
DELETE
FROM log_tokens lt
WHERE NOT EXISTS (SELECT 1
                  FROM log_items li
                  WHERE li.token_hash = lt.token_hash) RETURNING token_hash
    )
SELECT COUNT(*),
       ARRAY_AGG(token_hash) FILTER (WHERE token_hash IS NOT NULL)
INTO v_log_tokens_deleted, v_deleted_log_tokens
FROM orphaned_log_tokens;

-- Step 4: Delete orphaned command_tokens and CAPTURE the deleted hashes
WITH orphaned_command_tokens AS (
DELETE
FROM command_tokens ct
WHERE NOT EXISTS (SELECT 1
                  FROM commands c
                  WHERE c.token_hash = ct.token_hash) RETURNING token_hash
    )
SELECT COUNT(*),
       ARRAY_AGG(token_hash) FILTER (WHERE token_hash IS NOT NULL)
INTO v_command_tokens_deleted, v_deleted_command_tokens
FROM orphaned_command_tokens;

-- Return JSON result with counts and deleted token hashes
RETURN jsonb_build_object(
        'log_items_deleted', v_log_items_deleted,
        'commands_deleted', v_commands_deleted,
        'log_tokens_deleted', v_log_tokens_deleted,
        'command_tokens_deleted', v_command_tokens_deleted,
        'deleted_log_token_hashes', v_deleted_log_tokens,
        'deleted_command_token_hashes', v_deleted_command_tokens
       );
END;
$$;

COMMENT
ON FUNCTION delete_old_log_items IS 'Deletes log items, commands, and orphaned tokens (log_tokens, command_tokens), returns JSONB with counts and deleted token hashes. V44: Added return of deleted token hashes for Redis cleanup. V43: Added commands deletion. V42: Fixed project_key filtering.';
