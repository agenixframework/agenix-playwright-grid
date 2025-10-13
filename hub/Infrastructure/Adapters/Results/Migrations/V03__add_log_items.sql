-- ========================================
-- V23__add_log_items.sql
-- ========================================
-- Creates log_items table for storing test execution logs
-- Supports hierarchical relationship to test_items
-- ========================================

CREATE TABLE IF NOT EXISTS log_items
(
    -- Identity
    id                UUID PRIMARY KEY     DEFAULT gen_random_uuid(),

    -- Foreign Keys
    test_item_uuid    UUID        NOT NULL,
    launch_uuid       UUID        NOT NULL,

    -- Log Content
    level             TEXT        NOT NULL CHECK (level IN ('TRACE', 'DEBUG', 'INFO', 'WARN', 'ERROR', 'FATAL')),
    message           TEXT        NOT NULL,

    -- Metadata
    logger_name       TEXT        NULL,
    time              TIMESTAMPTZ NOT NULL,

    -- Optional Fields
    exception_type    TEXT        NULL,
    exception_message TEXT        NULL,
    stack_trace       TEXT        NULL,

    -- File References (for attachments)
    attachment_id     UUID        NULL,

    -- Timestamps
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at        TIMESTAMPTZ NULL,

    -- Foreign Key Constraint
    CONSTRAINT fk_log_items_test_item FOREIGN KEY (test_item_uuid)
        REFERENCES test_items (run_id) ON DELETE CASCADE
);

-- Indexes for performance
CREATE INDEX ix_log_items_test_item_uuid ON log_items (test_item_uuid);
CREATE INDEX ix_log_items_launch_uuid ON log_items (launch_uuid);
CREATE INDEX ix_log_items_level ON log_items (level);
CREATE INDEX ix_log_items_time ON log_items (time);
CREATE INDEX ix_log_items_logger_name ON log_items (logger_name) WHERE logger_name IS NOT NULL;

-- Comment
COMMENT ON TABLE log_items IS 'Stores log entries associated with test items during test execution';
