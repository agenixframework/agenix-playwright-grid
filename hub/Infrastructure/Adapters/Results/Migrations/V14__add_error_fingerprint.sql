-- ========================================
-- V28__add_error_fingerprint.sql
-- ========================================
-- Adds error_fingerprint column to log_tokens table for unique error grouping
-- Uses ReportPortal-inspired normalization techniques to group similar errors
-- ========================================

-- Add error_fingerprint column to log_tokens table
ALTER TABLE log_tokens
    ADD COLUMN error_fingerprint TEXT NULL;

-- Create index for fast grouping queries
-- Partial index: only index non-null fingerprints (ERROR/FATAL logs only)
CREATE INDEX ix_log_tokens_error_fingerprint
    ON log_tokens (error_fingerprint) WHERE error_fingerprint IS NOT NULL;

-- Add comment explaining the column purpose
COMMENT
ON COLUMN log_tokens.error_fingerprint IS
'Normalized error message for grouping similar errors. Generated from first line of ERROR/FATAL logs using ReportPortal-inspired normalization: datetime removal, log level stripping, camelCase splitting, number/UUID/hex normalization. Used for unique errors analysis across test runs.';
