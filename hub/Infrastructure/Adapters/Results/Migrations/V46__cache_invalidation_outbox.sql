-- V46: Cache Invalidation Outbox
-- Supports transactional cache invalidation to prevent stale data

CREATE TABLE IF NOT EXISTS cache_invalidation_outbox (
    id BIGSERIAL PRIMARY KEY,
    key TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Index for efficient polling
CREATE INDEX IF NOT EXISTS idx_cache_invalidation_outbox_created_at
    ON cache_invalidation_outbox(created_at);

-- Notify when a new record is added for near-real-time invalidation
CREATE OR REPLACE FUNCTION notify_cache_invalidation() RETURNS TRIGGER AS $$
BEGIN
    PERFORM pg_notify('cache_invalidation', NEW.key);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_notify_cache_invalidation ON cache_invalidation_outbox;
CREATE TRIGGER trigger_notify_cache_invalidation
    AFTER INSERT ON cache_invalidation_outbox
    FOR EACH ROW EXECUTE FUNCTION notify_cache_invalidation();
