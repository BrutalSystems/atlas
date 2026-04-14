namespace Atlas.Services.Jobs;

/// <summary>
/// Defines a background job. Implement this interface for each job type.
/// Caller identity (tenant, user) is captured separately in JobContext at enqueue time.
/// </summary>
public interface IJob
{
    /// <summary>
    /// Unique type identifier used for worker dispatch and serialization.
    /// Should match typeof(TJob).Name by convention.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// Wait groups this job participates in. Callers can await any of these groups via
    /// IJobOrchestrator.WaitForAsync. Multiple jobs sharing a group all must complete
    /// before that group's await resolves.
    /// </summary>
    WaitGroup[] WaitGroups { get; }

    /// <summary>
    /// When non-null, jobs with the same DebounceKey are coalesced: the timer resets on each
    /// new enqueue and only the latest job is ultimately published to the transport.
    /// Null means no debouncing — the job is sent to the transport immediately.
    /// </summary>
    string? DebounceKey { get; }

    /// <summary>
    /// Per-job override of the debounce window. Null uses the default registered for this job type.
    /// Only meaningful when DebounceKey is non-null.
    /// </summary>
    TimeSpan? DebounceWindow { get; }

    /// <summary>
    /// Default urgency for this job type. Can be overridden per-call in EnqueueJobAsync.
    /// Immediate bypasses the debounce window regardless of DebounceKey.
    /// </summary>
    JobUrgency Urgency => JobUrgency.Normal;
}
