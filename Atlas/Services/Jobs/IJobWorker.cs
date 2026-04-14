namespace Atlas.Services.Jobs;

/// <summary>
/// Non-generic base for job workers. Resolved by keyed DI using the job type name.
/// Implement IJobWorker&lt;TJob&gt; instead of this interface directly.
/// </summary>
public interface IJobWorker
{
    Task ProcessAsync(IJob job, CancellationToken ct);
}

/// <summary>
/// Typed job worker. Implement this for each job type; the non-generic ProcessAsync
/// is provided via default interface implementation.
/// </summary>
public interface IJobWorker<TJob> : IJobWorker where TJob : IJob
{
    Task ProcessAsync(TJob job, CancellationToken ct);

    Task IJobWorker.ProcessAsync(IJob job, CancellationToken ct) =>
        ProcessAsync((TJob)job, ct);
}
