-- V42: Fix retention cleanup functions to correctly join through launches for project_key filtering
-- Bug: Functions referenced non-existent project_key column in test_items and log_items tables
-- Fix: Join through launches table to filter by project_key
-- Date: 2025-12-09

-- ============================================================================
-- 1. Fix delete_old_log_items - Join through test_items → launches
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
    v_log_tokens_deleted
INT := 0;
    v_command_tokens_deleted
INT := 0;
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

-- Step 2: Delete orphaned log_tokens (tokens with no remaining log_items)
WITH orphaned_log_tokens AS (
DELETE
FROM log_tokens lt
WHERE NOT EXISTS (SELECT 1
                  FROM log_items li
                  WHERE li.token_hash = lt.token_hash) RETURNING token_hash
    )
SELECT COUNT(*)
INTO v_log_tokens_deleted
FROM orphaned_log_tokens;

-- Step 3: Delete orphaned command_tokens (tokens with no remaining references)
WITH orphaned_command_tokens AS (
DELETE
FROM command_tokens ct
WHERE NOT EXISTS (SELECT 1
                  FROM commands c
                  WHERE c.token_hash = ct.token_hash) RETURNING token_hash
    )
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

COMMENT
ON FUNCTION delete_old_log_items IS 'Deletes log items and orphaned tokens (log_tokens, command_tokens), returns JSONB with counts. V42: Fixed to join through launches for project_key filtering.';

-- ============================================================================
-- 2. Fix delete_old_attachments - Join through test_items → launches
-- ============================================================================
CREATE
OR REPLACE FUNCTION delete_old_attachments(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
v_result JSONB;
BEGIN
    -- Delete artifacts and return details for physical file deletion
    -- Join through test_items → launches for project filtering
WITH deleted_artifacts AS (
DELETE
FROM test_artifacts ta USING test_items ti
        JOIN launches l
ON ti.launch_id = l.id
WHERE ta.test_item_id = ti.run_id
  AND l.project_key = p_project_key
  AND ta.uploaded_at
    < p_cutoff_date
    RETURNING
    ta.id
    , ta.storage_path
    , ta.file_name
    , ta.file_size
    )
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

COMMENT
ON FUNCTION delete_old_attachments IS 'Hard deletes test_artifacts using uploaded_at timestamp and returns JSONB array with file details for physical deletion. V42: Fixed to join through launches for project_key filtering.';

-- ============================================================================
-- 3. Fix delete_old_audit_entries - Use correct timestamp column name
-- ============================================================================
CREATE
OR REPLACE FUNCTION delete_old_audit_entries(
    p_project_key TEXT,
    p_cutoff_date TIMESTAMPTZ
)
RETURNS INT
LANGUAGE plpgsql
AS $$
DECLARE
v_deleted_count INT;
BEGIN
    -- Delete audit entries using correct timestamp column name
WITH deleted AS (
DELETE
FROM audit_entries
WHERE project_key = p_project_key
  AND timestamp
    < p_cutoff_date
    RETURNING id
    )
SELECT COUNT(*)
INTO v_deleted_count
FROM deleted;

RETURN v_deleted_count;
END;
$$;

COMMENT
ON FUNCTION delete_old_audit_entries IS 'Deletes audit entries older than cutoff date for a specific project. V42: Fixed to use correct timestamp column name.';
