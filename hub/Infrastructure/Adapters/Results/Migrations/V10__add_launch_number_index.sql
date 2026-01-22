-- V10: Add index for launch number calculation performance optimization
-- This index optimizes the MAX(launch_number) query in CreateLaunch endpoint

CREATE INDEX IF NOT EXISTS ix_launches_project_name_number
    ON launches(project_key, name, launch_number DESC);

COMMENT
ON INDEX ix_launches_project_name_number IS
'Optimizes launch number calculation: SELECT MAX(launch_number) FROM launches WHERE project_key = ? AND name = ?';
