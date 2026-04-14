using System.Text.Json;

namespace Atlas.Services.Jobs;

/// <summary>
/// Maps job type names to their CLR types and registered debounce defaults.
/// Registered as a singleton; populated during startup via RegisterJob&lt;TJob, TWorker&gt;.
/// </summary>
public class JobTypeRegistry
{
    private readonly Dictionary<string, (Type JobType, TimeSpan? DefaultDebounce)> _types = [];

    public void Register<TJob>(TimeSpan? defaultDebounce = null) where TJob : IJob
    {
        _types[typeof(TJob).Name] = (typeof(TJob), defaultDebounce);
    }

    public IJob Deserialize(JobEnvelope envelope)
    {
        if (!_types.TryGetValue(envelope.JobType, out var info))
            throw new InvalidOperationException($"Unknown job type '{envelope.JobType}'. Did you call RegisterJob<{envelope.JobType}>?");

        return (IJob)JsonSerializer.Deserialize(envelope.PayloadJson, info.JobType)!;
    }

    public TimeSpan? GetDefaultDebounce(string jobType) =>
        _types.TryGetValue(jobType, out var info) ? info.DefaultDebounce : null;
}
