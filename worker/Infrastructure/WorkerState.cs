namespace WorkerService.Infrastructure;

/// <summary>
/// Represents the state of a worker in the system.
/// </summary>
/// <remarks>
/// The <see cref="WorkerState"/> class encapsulates configuration and state information
/// for a worker, primarily obtained from an instance of <see cref="WorkerOptions"/>.
/// </remarks>
public sealed class WorkerState(WorkerOptions options)
{
    /// <summary>
    /// Gets the worker options associated with the current worker state.
    /// </summary>
    /// <remarks>
    /// The property encapsulates the configuration parameters and settings used by a worker, represented by an instance of <see cref="WorkerOptions"/>.
    /// </remarks>
    public WorkerOptions Options { get; } = options;
}
