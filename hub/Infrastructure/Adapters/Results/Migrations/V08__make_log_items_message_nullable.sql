-- V8: Make log_items.message column nullable for token optimization
-- When USE_LOG_TOKEN_OPTIMIZATION=true, message is stored in log_tokens table
-- and only token_hash is stored in log_items.message can be NULL in this case.

ALTER TABLE log_items
    ALTER COLUMN message DROP NOT NULL;

-- Add comment to document this is intentional for token optimization
COMMENT ON COLUMN log_items.message IS 'Legacy message column (nullable when using token optimization - see token_hash column)';
