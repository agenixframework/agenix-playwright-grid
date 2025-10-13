-- V38: Flatten artifact storage paths to match V1 schema
-- ============================================================================
-- Problem: Artifact storage paths still use old three-level structure:
--   OLD: {runId}/{testId}/{guid}_{filename}
--   NEW: {testItemId}/{guid}_{filename}
--
-- V1 schema simplified test_artifacts to use single test_item_id FK
-- (no separate run_id and test_id columns), so storage paths should match.
--
-- This migration:
-- 1. Flattens paths by removing the middle {testId}/ directory level
-- 2. Uses test_item_id (which equals the first GUID in path) as new base
-- 3. Preserves the {guid}_{filename} part
--
-- Example transformation:
--   OLD: 7fe072e6-1222-41d9-bacb-0139b1494b9c/9e8b931e-374c-4f26-a9fa-599cc58f2adc/abc123_screenshot.png
--   NEW: 7fe072e6-1222-41d9-bacb-0139b1494b9c/abc123_screenshot.png
-- ============================================================================

-- Update storage paths: remove middle directory level
UPDATE test_artifacts
SET storage_path =
        -- Extract first GUID (test_item_id)
        SUBSTRING(storage_path FROM '^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/') || '/' ||
            -- Extract filename (everything after second slash)
        SUBSTRING(storage_path FROM
                  '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/(.+)$')
WHERE storage_path ~
      '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/.+$';

-- Log the update
DO
$$
    DECLARE
        updated_count INTEGER;
    BEGIN
        GET DIAGNOSTICS updated_count = ROW_COUNT;
        RAISE NOTICE 'Flattened % artifact storage paths from 3-level to 2-level structure', updated_count;
    END
$$;
