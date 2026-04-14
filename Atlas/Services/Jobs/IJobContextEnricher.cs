using Atlas.Mvc;

namespace Atlas.Services.Jobs;

/// <summary>
/// Optional per-app hook called by JobQueueWorker after SetJobContext.
/// Implementations can resolve UserEmail, UserName, or other identity details
/// from a data store using the AuthUserId already stamped on the UserContext.
/// </summary>
public interface IJobContextEnricher
{
    Task EnrichAsync(UserContext userContext, CancellationToken ct = default);
}
