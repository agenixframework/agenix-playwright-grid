-- ========================================
-- V16__audit_entries_table.sql
-- ========================================
-- Creates audit_entries table for system audit logging
-- Stores audit trail with transactional integrity
-- ========================================

CREATE TABLE IF NOT EXISTS audit_entries
(
    -- Identity
    id             BIGSERIAL PRIMARY KEY,

    -- Audit Information
    timestamp      TIMESTAMPTZ NOT NULL,
    category       TEXT        NOT NULL,
    action         TEXT        NOT NULL,

    -- Actor Information
    actor          TEXT        NULL,
    remote_ip      TEXT        NULL,

    -- Tracing
    correlation_id TEXT        NULL,

    -- Severity
    severity       TEXT        NOT NULL DEFAULT 'Info'
        CHECK (severity IN ('Trace', 'Debug', 'Info', 'Warning', 'Error', 'Critical')),

    -- Details (structured data)
    details        JSONB       NOT NULL DEFAULT '{}'
);

-- Indexes for performance
CREATE INDEX ix_audit_entries_timestamp ON audit_entries (timestamp DESC);
CREATE INDEX ix_audit_entries_category ON audit_entries (category);
CREATE INDEX ix_audit_entries_action ON audit_entries (action);
CREATE INDEX ix_audit_entries_actor ON audit_entries (actor) WHERE actor IS NOT NULL;
CREATE INDEX ix_audit_entries_severity ON audit_entries (severity);
CREATE INDEX ix_audit_entries_correlation_id ON audit_entries (correlation_id) WHERE correlation_id IS NOT NULL;

-- GIN index for details JSON queries
CREATE INDEX ix_audit_entries_details ON audit_entries USING GIN (details);

-- Comment
COMMENT ON TABLE audit_entries IS 'Stores system audit trail entries with transactional integrity and real-time notification support via pg_notify.';
