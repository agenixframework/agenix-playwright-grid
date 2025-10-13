-- V28: Fix artifact storage paths - remove duplicate 'artifacts/' prefix
-- ============================================================================
-- Problem: Artifact storage paths have duplicate 'artifacts/' directory:
--   OLD: artifacts/7fe072e6-1222-41d9-bacb-0139b1494b9c/9e8b931e-374c-4f26-a9fa-599cc58f2adc/error-screenshot.png
--   NEW: 7fe072e6-1222-41d9-bacb-0139b1494b9c/9e8b931e-374c-4f26-a9fa-599cc58f2adc/error-screenshot.png
--
-- This happened because:
-- 1. SaveArtifactAsync() generated path: "artifacts/{runId}/{testId}/{file}"
-- 2. Base path was already: "./data/artifacts"
-- 3. Combined: "./data/artifacts/artifacts/..." (duplicate!)
--
-- Fix: Remove the leading "artifacts/" prefix from all storage_path values
-- ============================================================================

UPDATE test_artifacts
SET storage_path = SUBSTRING(storage_path FROM 11) -- Remove 'artifacts/' (10 chars + 1 for /)
WHERE storage_path LIKE 'artifacts/%';

-- Log the update
DO
$$
DECLARE
updated_count INTEGER;
BEGIN
GET DIAGNOSTICS updated_count = ROW_COUNT;
RAISE
NOTICE 'Fixed % artifact storage paths by removing duplicate artifacts/ prefix', updated_count;
END $$;
