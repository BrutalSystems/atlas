using ByteAether.Ulid;

namespace Atlas.Services.Jobs;

/// <summary>
/// The payload published to the Foundatio IQueue transport.
/// Wraps a serialized IJob so the transport is decoupled from job types.
/// Must be a class (Foundatio IQueue&lt;T&gt; requires T : class).
/// </summary>
public class JobEnvelope
{
    public string JobId { get; init; } = Ulid.New().ToString();
    public required string JobType { get; init; }

    /// <summary>System.Text.Json serialization of the concrete IJob.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>Wait groups to decrement when this job completes or fails.</summary>
    public required WaitGroup[] WaitGroups { get; init; }

    /// <summary>Caller identity captured at enqueue time. Applied to the worker scope's UserContext.</summary>
    public required JobContext JobContext { get; init; }

    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}
