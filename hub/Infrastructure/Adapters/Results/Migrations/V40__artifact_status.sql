-- V40: Add status column to test_artifacts for async artifact upload tracking
-- Purpose: Support event-driven artifact uploads via ingestion service
-- Statuses: pending (upload queued), uploaded (complete), failed (upload error)

-- Add status column with default 'uploaded' for backward compatibility
ALTER TABLE test_artifacts
    ADD COLUMN status TEXT NOT NULL DEFAULT 'uploaded';

-- Add index for querying pending/failed uploads (monitoring)
CREATE INDEX ix_test_artifacts_status ON test_artifacts (status) WHERE status IN ('pending', 'failed');

-- Add constraint to enforce valid status values
ALTER TABLE test_artifacts
    ADD CONSTRAINT test_artifacts_status_check
        CHECK (status IN ('pending', 'uploaded', 'failed'));

-- Add comment for documentation
COMMENT
ON COLUMN test_artifacts.status IS 'Upload status: pending (queued), uploaded (complete), failed (error)';
