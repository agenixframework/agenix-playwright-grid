-- ========================================
-- V34__test_case_identity.sql
-- ========================================
-- Renames unique_id to test_case_id and adds test_case_hash for fast test history lookups
--
-- Changes:
-- 1. RENAME COLUMN unique_id → test_case_id
-- 2. ADD COLUMN test_case_hash INT4 (32-bit signed hash)
-- 3. ADD INDEX on (launch_id, test_case_hash) for fast history queries
-- 4. ADD INDEX on test_case_id for diagnostics
-- ========================================

-- Rename unique_id column to test_case_id
ALTER TABLE test_items
    RENAME COLUMN unique_id TO test_case_id;

-- Add hash column for fast lookups (32-bit signed integer)
ALTER TABLE test_items
    ADD COLUMN test_case_hash INT4 NOT NULL DEFAULT 0;

-- Add composite index for fast test history lookups by launch and hash
CREATE INDEX ix_test_items_test_case_hash ON test_items (launch_id, test_case_hash);

-- Add index on test_case_id for diagnostics and collision resolution
CREATE INDEX ix_test_items_test_case_id ON test_items (test_case_id) WHERE test_case_id IS NOT NULL;

-- Update column comments
COMMENT ON COLUMN test_items.test_case_id IS 'Canonical test case ID computed from testCaseId > codeRef > hierarchical path. Used for grouping test executions across launches for history tracking.';
COMMENT ON COLUMN test_items.test_case_hash IS '32-bit signed hash (xxHash32) of test_case_id for fast history lookups. Composite key (launch_id, test_case_hash) enables efficient queries.';
