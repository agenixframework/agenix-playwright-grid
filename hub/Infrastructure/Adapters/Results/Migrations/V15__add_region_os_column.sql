-- Add region_os column to test_items table
-- This stores the region/OS information for the browser session

ALTER TABLE test_items
    ADD COLUMN IF NOT EXISTS region_os TEXT NULL;

-- Add comment to document the column
COMMENT ON COLUMN test_items.region_os IS 'Region/OS information for the browser session (e.g., "us-east-1/macOS 10.15.7")';
