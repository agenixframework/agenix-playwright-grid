-- V9: Add db_id column to test_items table for sequential ordering
-- This provides a monotonically increasing ID for pagination and ordering

ALTER TABLE test_items
    ADD COLUMN IF NOT EXISTS db_id BIGSERIAL;

-- Create index for efficient queries on db_id
CREATE INDEX IF NOT EXISTS ix_test_items_db_id ON test_items (db_id);

-- Add comment to document the purpose of db_id
COMMENT ON COLUMN test_items.db_id IS 'Sequential ID for ordering and pagination (auto-incrementing)';
