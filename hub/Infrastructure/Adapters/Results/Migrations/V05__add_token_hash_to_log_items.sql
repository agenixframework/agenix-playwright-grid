-- ========================================
-- V27__add_token_hash_to_log_items.sql
-- ========================================
-- Adds token_hash column to log_items table
-- Links log items to deduplicated log_tokens for optimization
-- ========================================

-- Add token_hash column
ALTER TABLE log_items
    ADD COLUMN IF NOT EXISTS token_hash TEXT NULL;

-- Add foreign key constraint to log_tokens
ALTER TABLE log_items
    ADD CONSTRAINT fk_log_items_token_hash
        FOREIGN KEY (token_hash) REFERENCES log_tokens (token_hash) ON DELETE SET NULL;

-- Add index for performance
CREATE INDEX IF NOT EXISTS ix_log_items_token_hash ON log_items (token_hash) WHERE token_hash IS NOT NULL;

-- Comment
COMMENT ON COLUMN log_items.token_hash IS 'Reference to deduplicated log token. When set, the full message is stored in log_tokens table to reduce storage duplication.';
