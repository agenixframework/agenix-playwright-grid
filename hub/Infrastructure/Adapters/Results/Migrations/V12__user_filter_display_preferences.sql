-- ========================================
-- V12__user_filter_display_preferences.sql
-- ========================================
--
-- This migration adds the user_filter_display_preferences table
-- which was missing from V1__init.sql consolidation.
--
-- Purpose: Track per-user display preferences for individual filters.
-- This is separate from user_filter_preferences which tracks the selected filter.
--
-- Schema Version: V12
-- Created: 2025-11-15
-- Migration Tool: DbUp
-- ========================================

-- ----------------------------------------
-- Table: user_filter_display_preferences
-- Per-user display settings for individual filters (show/hide on launches page)
-- ----------------------------------------
CREATE TABLE IF NOT EXISTS user_filter_display_preferences
(
    -- Composite key
    user_id             TEXT        NOT NULL,
    filter_id           UUID        NOT NULL REFERENCES launch_filters (id) ON DELETE CASCADE,

    -- Display preference
    display_on_launches BOOLEAN     NOT NULL DEFAULT TRUE,

    -- Timestamp
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    PRIMARY KEY (user_id, filter_id)
);

-- User Filter Display Preferences Indexes
CREATE INDEX IF NOT EXISTS ix_user_filter_display_preferences_filter_id ON user_filter_display_preferences (filter_id);
CREATE INDEX IF NOT EXISTS ix_user_filter_display_preferences_user_id ON user_filter_display_preferences (user_id);

-- User Filter Display Preferences Comments
COMMENT ON TABLE user_filter_display_preferences IS 'Per-user display preferences for individual launch filters';
COMMENT ON COLUMN user_filter_display_preferences.user_id IS 'User identifier';
COMMENT ON COLUMN user_filter_display_preferences.filter_id IS 'Filter identifier (FK to launch_filters)';
COMMENT ON COLUMN user_filter_display_preferences.display_on_launches IS 'Whether this user wants to see this filter on the launches page';
COMMENT ON COLUMN user_filter_display_preferences.updated_at IS 'Last time this preference was updated';

-- ========================================
-- MIGRATION COMPLETE
-- ========================================
