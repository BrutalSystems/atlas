using System.Collections.Concurrent;
using System.Text.Json;
using Atlas.Mvc;
using Foundatio.Queues;
using Microsoft.Extensions.Logging;

namespace Atlas.Services.Jobs;

public class JobOrchestrator(
    IQueue<JobEnvelope> queue,
    JobTypeRegistry registry,
    TimeSpan defaultDebounce,
    ILogger<JobOrchestrator> logger) : IJobOrchestrator, IAsyncDisposable
{
    // ── Wait group tracking ──────────────────────────────────────────────────

    /// <summary>
    /// Thread-safe counter + TCS per wait group. When the count drains to 0 the TCS
    /// fires, then resets for the next round of jobs.
    /// </summary>
    private sealed class WaitGroupState
    {
        private int _count;
        private TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Increment()
        {
            lock (this) { _count++; }
        }

        public void Decrement()
        {
            TaskCompletionSource<bool>? toFire = null;
            lock (this)
            {
                if (--_count <= 0)
                {
                    _count = 0;
                    toFire = _tcs;
                    _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            toFire?.TrySetResult(true); // outside lock — avoids inline continuation deadlock
        }

        /// <summary>Returns awaitable task, or null if nothing is pending right now.</summary>
        public Task? WaitIfPendingAsync(CancellationToken ct)
        {
            lock (this)
            {
                if (_count == 0) return null;
                return ct.CanBeCanceled ? _tcs.Task.WaitAsync(ct) : _tcs.Task;
            }
        }
    }

    private readonly ConcurrentDictionary<string, WaitGroupState> _waitGroups = new();

    // ── Debounce tracking ────────────────────────────────────────────────────

    private sealed class DebounceEntry
    {
        public required IJob Job { get; set; }                      // replaced on each reset
        public required JobContext JobContext { get; set; }         // replaced on each reset (latest caller wins)
        public required Timer Timer { get; init; }
        public required WaitGroup[] WaitGroups { get; init; }       // stable — set on first enqueue
    }

    private readonly ConcurrentDictionary<string, DebounceEntry> _debounce = new();
    private readonly CancellationTokenSource _cts = new();

    // ── IJobOrchestrator ─────────────────────────────────────────────────────

    public void Enqueue(IJob job, JobContext jobContext, JobUrgency urgency = JobUrgency.Normal)
    {
        if (urgency == JobUrgency.Immediate || job.DebounceKey is null)
        {
            // Immediate urgency or no debounce key — hit the transport right away
            IncrementWaitGroups(job.WaitGroups);
            FireToTransport(job, jobContext);
            return;
        }

        var window = job.DebounceWindow
            ?? registry.GetDefaultDebounce(job.JobType)
            ?? defaultDebounce;

        _debounce.AddOrUpdate(
            job.DebounceKey,
            addValueFactory: key =>
            {
                IncrementWaitGroups(job.WaitGroups);
                var timer = new Timer(_ => PublishDebounced(key), null, window, Timeout.InfiniteTimeSpan);
                return new DebounceEntry { Job = job, JobContext = jobContext, Timer = timer, WaitGroups = job.WaitGroups };
            },
            updateValueFactory: (_, existing) =>
            {
                existing.Job = job;              // replace with latest
                existing.JobContext = jobContext; // latest caller context wins
                existing.Timer.Change(window, Timeout.InfiniteTimeSpan); // reset timer
                return existing;                 // same wait groups — no re-increment
            });
    }

    public Task WaitForAsync(WaitGroup group, CancellationToken ct = default)
    {
        if (!_waitGroups.TryGetValue(GroupKey(group), out var state))
            return Task.CompletedTask;
        return state.WaitIfPendingAsync(ct) ?? Task.CompletedTask;
    }

    public void NotifyCompleted(WaitGroup[] groups) => DecrementWaitGroups(groups);

    public void NotifyFailed(WaitGroup[] groups, Exception ex)
    {
        logger.LogError(ex, "Background job failed; unblocking {Count} wait group(s)", groups.Length);
        DecrementWaitGroups(groups);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void PublishDebounced(string debounceKey)
    {
        if (!_debounce.TryRemove(debounceKey, out var entry)) return;
        entry.Timer.Dispose();
        FireToTransport(entry.Job, entry.JobContext);
    }

    private void FireToTransport(IJob job, JobContext jobContext)
    {
        var envelope = new JobEnvelope
        {
            JobType     = job.JobType,
            PayloadJson = JsonSerializer.Serialize(job, job.GetType()),
            WaitGroups  = job.WaitGroups,
            JobContext  = jobContext,
        };

        _ = Task.Run(async () =>
        {
            try { await queue.EnqueueAsync(envelope); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enqueue job {JobType}", job.JobType);
                DecrementWaitGroups(job.WaitGroups); // unblock waiters even on enqueue failure
            }
        });
    }

    private void IncrementWaitGroups(WaitGroup[] groups)
    {
        foreach (var g in groups)
            _waitGroups.GetOrAdd(GroupKey(g), _ => new WaitGroupState()).Increment();
    }

    private void DecrementWaitGroups(WaitGroup[] groups)
    {
        foreach (var g in groups)
        {
            if (_waitGroups.TryGetValue(GroupKey(g), out var state))
                state.Decrement();
        }
    }

    private static string GroupKey(WaitGroup g) => $"{g.Type}:{g.Key}";

    // ── IAsyncDisposable ─────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        // Cancel all pending debounce timers and unblock waiters
        foreach (var key in _debounce.Keys.ToList())
        {
            if (_debounce.TryRemove(key, out var entry))
            {
                await entry.Timer.DisposeAsync();
                DecrementWaitGroups(entry.WaitGroups);
            }
        }

        _cts.Dispose();
    }
}
