-- ========================================
-- V20__add_project_key_to_audit_entries.sql
-- ========================================
-- Adds project_key column to audit_entries table
-- Required for multi-project audit trail filtering
-- ========================================

ALTER TABLE audit_entries
    ADD COLUMN IF NOT EXISTS project_key TEXT NULL;

-- Index for project-level audit queries
CREATE INDEX IF NOT EXISTS ix_audit_entries_project_key
    ON audit_entries (project_key) WHERE project_key IS NOT NULL;

-- Comment
COMMENT ON COLUMN audit_entries.project_key IS 'Project key for filtering audit entries by project. NULL for system-level audit entries.';
