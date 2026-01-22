-- Add index on parent_item_id for hierarchical queries
-- This improves performance when loading child test items (steps)

CREATE INDEX IF NOT EXISTS ix_test_items_parent_item_id
    ON test_items(parent_item_id)
    WHERE parent_item_id IS NOT NULL;

-- Add composite index for common query pattern (parent + start_time ordering)
CREATE INDEX IF NOT EXISTS ix_test_items_parent_start
    ON test_items(parent_item_id, start_time)
    WHERE parent_item_id IS NOT NULL;
