-- V45: History Matrix Functions
-- Creates optimized queries for launch and suite history matrices

-- Function: Get launch-level history (parent items like Suites, Stories)
CREATE
OR REPLACE FUNCTION get_launch_parent_items_history(
    p_project_key TEXT,
    p_depth INT DEFAULT 10
) RETURNS TABLE (
    item_name TEXT,
    item_type TEXT,
    launches JSONB
) AS $$
BEGIN
RETURN QUERY WITH recent_launches AS (
        SELECT l.id, l.launch_number, l.start_time
        FROM launches l
        WHERE l.project_key = p_project_key
          AND l.status IN ('Finished', 'Failed', 'Stopped', 'InProgress')
        ORDER BY l.launch_number DESC
        LIMIT p_depth
    ),
    parent_items AS (
        SELECT DISTINCT ti.name, ti.item_type
        FROM test_items ti
        WHERE ti.launch_id IN (SELECT id FROM recent_launches)
          AND ti.item_type IN ('Suite', 'Story', 'Scenario', 'BeforeSuite', 'AfterSuite', 'BeforeClass', 'AfterClass')
          AND ti.parent_item_id IS NULL
        ORDER BY ti.name
    )
SELECT pi.name,
       pi.item_type,
       jsonb_agg(
               jsonb_build_object(
                       'launchId', rl.id,
                       'launchNumber', rl.launch_number,
                       'startTime', rl.start_time,
                       'status', COALESCE(
                               (SELECT CASE
                                           WHEN COUNT(*) FILTER (WHERE ti.computed_status IN ('Failed', 'Errored')) > 0 THEN 'Failed'
                                           WHEN COUNT(*) FILTER (WHERE ti.computed_status = 'Passed') = COUNT(*)
                                   AND COUNT(*) > 0 THEN 'Passed'
                               WHEN COUNT(*) FILTER(WHERE ti.computed_status = 'InProgress') > 0 THEN 'InProgress'
                               WHEN COUNT(*) FILTER(WHERE ti.computed_status = 'Skipped') = COUNT(*)
                                   AND COUNT(*) > 0 THEN 'Skipped'
                               WHEN COUNT(*) > 0 THEN 'Mixed'
                               ELSE 'Empty'
                               END
                               FROM test_items ti
                               WHERE ti.launch_id = rl.id
                                   AND ti.name = pi.name
                                   AND ti.item_type = pi.item_type
                                   AND ti.parent_item_id IS NULL),
                    'Empty'
                ),
                       'tooltip', COALESCE(
                               (SELECT jsonb_build_object(
                                               'sessionStatus', ti.session_status,
                                               'computedStatus', ti.computed_status,
                                               'total', COUNT(*),
                                               'passed', COUNT(*) FILTER (WHERE ti.computed_status = 'Passed'),
                                               'failed',
                                               COUNT(*) FILTER (WHERE ti.computed_status IN ('Failed', 'Errored')),
                                               'skipped', COUNT(*) FILTER (WHERE ti.computed_status = 'Skipped')
                                       )
                                FROM test_items ti
                                WHERE ti.launch_id = rl.id
                                  AND ti.name = pi.name
                                  AND ti.item_type = pi.item_type
                                  AND ti.parent_item_id IS NULL
                                GROUP BY ti.session_status, ti.computed_status
                               LIMIT 1),
                    '{}'::jsonb
                )
               ) ORDER BY rl.launch_number DESC
       ) AS launches
FROM parent_items pi
         CROSS JOIN recent_launches rl
GROUP BY pi.name, pi.item_type
ORDER BY pi.name;
END;
$$
LANGUAGE plpgsql;

-- Function: Get suite-level history (child items like Tests, Scenarios)
CREATE
OR REPLACE FUNCTION get_suite_child_items_history(
    p_suite_db_id BIGINT,
    p_depth INT DEFAULT 10
) RETURNS TABLE (
    item_name TEXT,
    item_type TEXT,
    launches JSONB
) AS $$
DECLARE
v_launch_id UUID;
    v_project_key
TEXT;
    v_suite_name
TEXT;
BEGIN
    -- Get launch_id, project_key and suite name from suite
SELECT ti.launch_id, l.project_key, ti.name
INTO v_launch_id, v_project_key, v_suite_name
FROM test_items ti
         JOIN launches l ON l.id = ti.launch_id
WHERE ti.db_id = p_suite_db_id;

IF
NOT FOUND THEN
        -- Return empty result set instead of raising exception
        RETURN;
END IF;

RETURN QUERY WITH recent_launches AS (
        SELECT l.id, l.launch_number, l.start_time
        FROM launches l
        WHERE l.project_key = v_project_key
          AND l.status IN ('Finished', 'Failed', 'Stopped', 'InProgress')
        ORDER BY l.launch_number DESC
        LIMIT p_depth
    ),
    suite_items AS (
        SELECT ti.run_id AS suite_id
        FROM test_items ti
        WHERE ti.launch_id IN (SELECT id FROM recent_launches)
          AND ti.name = v_suite_name
          AND ti.item_type IN ('Suite', 'Story')
          AND ti.parent_item_id IS NULL
    ),
    child_items AS (
        SELECT DISTINCT ti.name, ti.item_type
        FROM test_items ti
        WHERE ti.parent_item_id IN (SELECT suite_id FROM suite_items)
          AND ti.item_type IN ('Test', 'Scenario', 'BeforeTest', 'AfterTest', 'BeforeMethod', 'AfterMethod')
        ORDER BY ti.name
    )
SELECT ci.name,
       ci.item_type,
       jsonb_agg(
               jsonb_build_object(
                       'launchId', rl.id,
                       'launchNumber', rl.launch_number,
                       'startTime', rl.start_time,
                       'status', COALESCE(
                               (SELECT CASE
                                           WHEN COUNT(*) FILTER (WHERE ti.computed_status IN ('Failed', 'Errored')) > 0 THEN 'Failed'
                                           WHEN COUNT(*) FILTER (WHERE ti.computed_status = 'Passed') = COUNT(*)
                                   AND COUNT(*) > 0 THEN 'Passed'
                               WHEN COUNT(*) FILTER(WHERE ti.computed_status = 'InProgress') > 0 THEN 'InProgress'
                               WHEN COUNT(*) FILTER(WHERE ti.computed_status = 'Skipped') = COUNT(*)
                                   AND COUNT(*) > 0 THEN 'Skipped'
                               WHEN COUNT(*) > 0 THEN 'Mixed'
                               ELSE 'Empty'
                               END
                               FROM test_items ti
                               JOIN test_items suite ON suite.launch_id = rl.id
                                   AND suite.name = v_suite_name
                                   AND suite.item_type IN ('Suite', 'Story')
                                   AND suite.parent_item_id IS NULL
                               WHERE ti.parent_item_id = suite.run_id
                                   AND ti.name = ci.name
                                   AND ti.item_type = ci.item_type),
                    'Empty'
                ),
                       'tooltip', COALESCE(
                               (SELECT jsonb_build_object(
                                               'sessionStatus', ti.session_status,
                                               'computedStatus', ti.computed_status,
                                               'total', COUNT(*),
                                               'passed', COUNT(*) FILTER (WHERE ti.computed_status = 'Passed'),
                                               'failed',
                                               COUNT(*) FILTER (WHERE ti.computed_status IN ('Failed', 'Errored')),
                                               'skipped', COUNT(*) FILTER (WHERE ti.computed_status = 'Skipped')
                                       )
                                FROM test_items ti
                                         JOIN test_items suite ON suite.launch_id = rl.id
                                    AND suite.name = v_suite_name
                                    AND suite.item_type IN ('Suite', 'Story')
                                    AND suite.parent_item_id IS NULL
                                WHERE ti.parent_item_id = suite.run_id
                                  AND ti.name = ci.name
                                  AND ti.item_type = ci.item_type
                                GROUP BY ti.session_status, ti.computed_status
                               LIMIT 1),
                    '{}'::jsonb
                )
               ) ORDER BY rl.launch_number DESC
       ) AS launches
FROM child_items ci
         CROSS JOIN recent_launches rl
GROUP BY ci.name, ci.item_type
ORDER BY ci.name;
END;
$$
LANGUAGE plpgsql;

-- Create composite indexes for performance
CREATE INDEX IF NOT EXISTS idx_test_items_launch_item_type_parent
    ON test_items(launch_id, item_type, parent_item_id);

CREATE INDEX IF NOT EXISTS idx_test_items_parent_name_type
    ON test_items(parent_item_id, name, item_type)
    WHERE parent_item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_launches_project_status_number
    ON launches(project_key, status, launch_number DESC);
