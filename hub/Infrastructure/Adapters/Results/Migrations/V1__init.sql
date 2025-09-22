-- V1__init.sql: Initial schema for durable Postgres results store
-- Tables: runs, tests, commands; TTL via expires_at; useful indexes

CREATE TABLE IF NOT EXISTS runs (
    run_id TEXT PRIMARY KEY,
    run_json TEXT NOT NULL,
    app TEXT NULL,
    browser TEXT NULL,
    env TEXT NULL,
    status TEXT NULL,
    started_at_utc TIMESTAMPTZ NULL,
    completed_at_utc TIMESTAMPTZ NULL,
    expires_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_runs_by_start ON runs(started_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_runs_expires ON runs(expires_at);

CREATE TABLE IF NOT EXISTS tests (
    run_id TEXT NOT NULL,
    test_id TEXT NOT NULL,
    test_json TEXT NOT NULL,
    status TEXT NULL,
    title TEXT NULL,
    expires_at TIMESTAMPTZ NULL,
    PRIMARY KEY (run_id, test_id)
);
CREATE INDEX IF NOT EXISTS ix_tests_by_run ON tests(run_id);
CREATE INDEX IF NOT EXISTS ix_tests_by_run_status ON tests(run_id, status);
CREATE INDEX IF NOT EXISTS ix_tests_expires ON tests(expires_at);

CREATE TABLE IF NOT EXISTS commands (
    run_id TEXT NOT NULL,
    timestamp_utc TIMESTAMPTZ NOT NULL,
    kind TEXT NULL,
    message TEXT NULL,
    props_json TEXT NULL,
    test_id TEXT NULL,
    expires_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_commands_by_run_time ON commands(run_id, timestamp_utc);
CREATE INDEX IF NOT EXISTS ix_commands_expires ON commands(expires_at);
