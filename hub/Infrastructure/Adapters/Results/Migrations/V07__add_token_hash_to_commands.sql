-- ========================================
-- V7__add_token_hash_to_commands.sql
-- ========================================
-- Adds token_hash column to commands table
-- Links commands to deduplicated command_tokens for optimization
-- ========================================

-- Add token_hash column
ALTER TABLE commands
    ADD COLUMN IF NOT EXISTS token_hash TEXT NULL;

-- Add foreign key constraint to command_tokens
ALTER TABLE commands
    ADD CONSTRAINT fk_commands_token_hash
        FOREIGN KEY (token_hash) REFERENCES command_tokens (token_hash) ON DELETE SET NULL;

-- Add index for performance
CREATE INDEX IF NOT EXISTS ix_commands_token_hash ON commands(token_hash) WHERE token_hash IS NOT NULL;

-- Comment
COMMENT
ON COLUMN commands.token_hash IS 'Reference to deduplicated command token. When set, the full message is stored in command_tokens table to reduce storage duplication.';
