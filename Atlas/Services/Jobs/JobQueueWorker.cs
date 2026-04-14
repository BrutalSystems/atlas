using Atlas.Mvc;
using Foundatio.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Services.Jobs;

/// <summary>
/// Hosted service that consumes JobEnvelopes from the Foundatio queue and dispatches
/// each to the appropriate IJobWorker resolved from a per-job DI scope.
/// Stamps the worker scope's UserContext with the job's JobContext so that EF tenant
/// filters and audit fields work correctly in background execution.
/// </summary>
public class JobQueueWorker(
    IQueue<JobEnvelope> queue,
    IJobOrchestrator orchestrator,
    JobTypeRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<JobQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("JobQueueWorker started");

        while (!ct.IsCancellationRequested)
        {
            IQueueEntry<JobEnvelope>? entry = null;
            try
            {
                entry = await queue.DequeueAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error dequeuing job");
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                continue;
            }

            if (entry is null) continue;

            try
            {
                var job = registry.Deserialize(entry.Value);

                using var scope = scopeFactory.CreateScope();

                // Stamp the scoped UserContext with the enqueuing caller's identity so that
                // EF tenant filters apply correctly and audit fields (CreatedBy/UpdatedBy) are set.
                var userContext = scope.ServiceProvider.GetService<UserContext>();
                userContext?.SetJobContext(entry.Value.JobContext);

                // Allow the app to enrich UserEmail/UserName from a data store (optional).
                var enricher = scope.ServiceProvider.GetService<IJobContextEnricher>();
                if (enricher != null && userContext != null)
                    await enricher.EnrichAsync(userContext, ct);

                var worker = scope.ServiceProvider.GetRequiredKeyedService<IJobWorker>(entry.Value.JobType);

                await worker.ProcessAsync(job, ct);
                await entry.CompleteAsync();

                orchestrator.NotifyCompleted(entry.Value.WaitGroups);
                logger.LogDebug("Job {JobType}/{JobId} completed", entry.Value.JobType, entry.Value.JobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {JobType}/{JobId} failed", entry.Value.JobType, entry.Value.JobId);
                try { await entry.AbandonAsync(); } catch { /* best-effort */ }
                orchestrator.NotifyFailed(entry.Value.WaitGroups, ex);
            }
        }

        logger.LogInformation("JobQueueWorker stopped");
    }
}
