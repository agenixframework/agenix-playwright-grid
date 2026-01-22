-- ========================================
-- V28__command_tokens.sql
-- ========================================
-- Creates command_tokens table for command message deduplication
-- Used by ingestion service for command token optimization
-- ========================================

CREATE TABLE IF NOT EXISTS command_tokens
(
    -- Identity (token hash is the primary key)
    token_hash       TEXT PRIMARY KEY,

    -- Command Content
    message          TEXT        NOT NULL,
    kind             TEXT        NULL, -- Command type/kind

    -- Metadata (for future extensibility, stores common metadata only)
    metadata_json    JSONB       NULL,

    -- Tracking
    first_seen_at    TIMESTAMPTZ NOT NULL,
    last_seen_at     TIMESTAMPTZ NOT NULL,
    occurrence_count INTEGER     NOT NULL DEFAULT 1
);

-- Indexes for performance
CREATE INDEX ix_command_tokens_kind ON command_tokens (kind) WHERE kind IS NOT NULL;
CREATE INDEX ix_command_tokens_first_seen_at ON command_tokens (first_seen_at);
CREATE INDEX ix_command_tokens_last_seen_at ON command_tokens (last_seen_at);
CREATE INDEX ix_command_tokens_occurrence_count ON command_tokens (occurrence_count DESC);

-- GIN index for metadata JSON queries
CREATE INDEX ix_command_tokens_metadata_json ON command_tokens USING GIN (metadata_json) WHERE metadata_json IS NOT NULL;

-- Comment
COMMENT ON TABLE command_tokens IS 'Stores deduplicated command message tokens for optimization. Used by ingestion service to reduce storage of repetitive command messages.';
