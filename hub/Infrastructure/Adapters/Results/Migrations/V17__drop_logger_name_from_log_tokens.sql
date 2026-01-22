-- ========================================
-- V11__drop_logger_name_from_log_tokens.sql
-- ========================================
-- Drops logger_name column from log_tokens table
-- ========================================

-- Drop index first
DROP INDEX IF EXISTS ix_log_tokens_logger_name;

-- Drop logger_name column
ALTER TABLE log_tokens
    DROP COLUMN IF EXISTS logger_name;

-- Update comment
COMMENT ON TABLE log_tokens IS 'Stores deduplicated log message tokens for optimization. Used by ingestion service to reduce storage of repetitive log messages. Token hash is based only on message content.';
