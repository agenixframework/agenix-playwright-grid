# Browser Run Cleanup Service

The `RunCleanupService` is a background service that monitors and automatically cleans up stalled or long-running test runs, ensuring proper resource management in the PlaywrightHub system.

## Cleanup Parameters

The service uses two primary time-based parameters to determine when to clean up test runs:

### Inactivity Threshold

**Configuration Key:** `HUB_INACTIVITY_THRESHOLD_MINUTES`  
**Default Value:** 30 minutes

The inactivity threshold defines how long a run can remain idle (no commands being executed) before it becomes eligible for cleanup. This parameter is designed to catch scenarios where:

- A test has stalled or frozen
- A user started a test but abandoned it
- A process crashed without properly releasing browsers
- Network interruptions prevented normal test termination

When a run has no activity for longer than this threshold, the service will attempt to release any outstanding browser resources and mark the run as auto-stopped.

### Maximum Duration

**Configuration Key:** `HUB_MAX_RUN_DURATION_HOURS`  
**Default Value:** 3 hours  
**Override Key:** `HUB_MAX_RUN_DURATION_MINUTES` (for finer control)

The maximum duration sets an absolute time limit on how long any test run can execute, regardless of whether it's actively running commands. This parameter prevents scenarios where:

- Tests are caught in infinite loops but still executing commands
- Long-running tests hog browser resources indefinitely
- Resource leaks accumulate over extended periods

When a run exceeds this total duration, it will be terminated even if it's still actively executing commands.

## Why Two Parameters Are Necessary

Having both inactivity and maximum duration parameters provides a comprehensive approach to resource management:

1. **Different Failure Modes:** Tests can fail in various ways - some by stalling (caught by inactivity), others by never completing (caught by max duration)

2. **Resource Optimization:** Browser instances are resource-intensive; both parameters ensure they don't remain allocated unnecessarily

3. **Operational Flexibility:** Administrators can tune these parameters separately based on their specific testing needs and infrastructure capabilities

## Cleanup Logic

A test run becomes eligible for cleanup when:
- It has been inactive for longer than the inactivity threshold **OR**
- It has been running for longer than the maximum duration
- **AND** it has outstanding browser instances that haven't been properly released

## Additional Configuration Parameters

- `HUB_CLEANUP_INTERVAL_MINUTES`: How frequently the cleanup service checks for candidates (default: 5 minutes)
- `HUB_CLEANUP_BATCH_SIZE`: Maximum number of runs to process in each interval (default: 50)
- `HUB_CLEANUP_DEBUG`: Enable verbose debug logging for the cleanup process (default: false)

## Best Practices

1. **Set Appropriate Values:** Consider your test complexity and typical durations when configuring these parameters

2. **Monitor Cleanup Actions:** Enable debug logging initially to ensure the cleanup service is working as expected

3. **Environment-Specific Settings:** Development environments may benefit from shorter thresholds than production

4. **Graceful Termination:** Always try to properly end test runs rather than relying on the cleanup service
