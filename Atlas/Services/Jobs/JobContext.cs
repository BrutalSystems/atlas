namespace Atlas.Services.Jobs;

/// <summary>
/// Captures the caller's identity at the moment a job is enqueued.
/// Carried in JobEnvelope and applied to the worker scope's UserContext so that
/// background jobs run with the correct tenant isolation and audit identity.
/// </summary>
public record JobContext(string? TenantId, string? AuthUserId);
