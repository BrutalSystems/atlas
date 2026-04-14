namespace Atlas.Services.Jobs;

/// <summary>
/// Central interface for enqueueing background jobs and awaiting their completion.
/// Handles debouncing, wait-group tracking, and publishes to the Foundatio transport.
/// </summary>
public interface IJobOrchestrator
{
    /// <summary>
    /// Enqueues a job with caller context. If the job has a DebounceKey, rapid re-enqueues
    /// coalesce into a single execution — the timer resets and the latest job replaces the
    /// previous one. Non-debounced jobs are sent to the transport immediately.
    /// Pass urgency = Immediate to bypass the debounce window regardless of DebounceKey.
    /// </summary>
    void Enqueue(IJob job, JobContext jobContext, JobUrgency urgency = JobUrgency.Normal);

    /// <summary>
    /// Returns a Task that completes when all currently-pending jobs registered under
    /// the given wait group have finished. Returns a completed Task if nothing is pending.
    /// </summary>
    Task WaitForAsync(WaitGroup group, CancellationToken ct = default);

    /// <summary>Convenience overload.</summary>
    Task WaitForAsync(string groupType, string groupKey, CancellationToken ct = default)
        => WaitForAsync(new WaitGroup(groupType, groupKey), ct);

    /// <summary>Called by JobQueueWorker after a job succeeds.</summary>
    void NotifyCompleted(WaitGroup[] groups);

    /// <summary>Called by JobQueueWorker after a job fails. Still unblocks waiters.</summary>
    void NotifyFailed(WaitGroup[] groups, Exception ex);
}
