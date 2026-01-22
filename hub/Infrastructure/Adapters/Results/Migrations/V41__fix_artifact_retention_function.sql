-- V41: Fix delete_old_attachments to use correct column names
-- Bug: Function referenced non-existent 'created_at' and wrong FK column name 'test_item_uuid'
-- Fix: Use 'uploaded_at' (existing timestamp) and 'test_item_id' (correct FK column name)
-- Date: 2025-12-08

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
    -- Fixed: Use test_item_id (not test_item_uuid) and uploaded_at (not created_at)
WITH deleted_artifacts AS (
DELETE
FROM test_artifacts ta USING test_items ti
WHERE ta.test_item_id = ti.run_id -- ✅ Fixed: correct FK column name
  AND ti.project_key = p_project_key
  AND ta.uploaded_at
    < p_cutoff_date               -- ✅ Fixed: use existing timestamp column
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
ON FUNCTION delete_old_attachments IS 'Hard deletes test_artifacts using uploaded_at timestamp and returns JSONB array with file details for physical deletion (V41: Fixed column names)';
