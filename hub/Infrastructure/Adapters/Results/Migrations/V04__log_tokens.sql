-- ========================================
-- V24__log_tokens.sql
-- ========================================
-- Creates log_tokens table for log message deduplication
-- Used by ingestion service for log token optimization
-- ========================================

CREATE TABLE IF NOT EXISTS log_tokens
(
    -- Identity (token hash is the primary key)
    token_hash       TEXT PRIMARY KEY,

    -- Log Content
    message          TEXT        NOT NULL,
    logger_name      TEXT        NOT NULL,

    -- Metadata (for future extensibility)
    metadata_json    JSONB       NULL,

    -- Tracking
    first_seen_at    TIMESTAMPTZ NOT NULL,
    last_seen_at     TIMESTAMPTZ NOT NULL,
    occurrence_count INTEGER     NOT NULL DEFAULT 1
);

-- Indexes for performance
CREATE INDEX ix_log_tokens_logger_name ON log_tokens (logger_name);
CREATE INDEX ix_log_tokens_first_seen_at ON log_tokens (first_seen_at);
CREATE INDEX ix_log_tokens_last_seen_at ON log_tokens (last_seen_at);
CREATE INDEX ix_log_tokens_occurrence_count ON log_tokens (occurrence_count DESC);

-- GIN index for metadata JSON queries
CREATE INDEX ix_log_tokens_metadata_json ON log_tokens USING GIN (metadata_json) WHERE metadata_json IS NOT NULL;

-- Comment
COMMENT ON TABLE log_tokens IS 'Stores deduplicated log message tokens for optimization. Used by ingestion service to reduce storage of repetitive log messages.';
