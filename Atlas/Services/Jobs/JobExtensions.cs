using Foundatio.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Services.Jobs;

public static class JobExtensions
{
    /// <summary>
    /// Registers the job orchestration infrastructure: JobTypeRegistry, IJobOrchestrator,
    /// IQueue&lt;JobEnvelope&gt; (in-memory), and the JobQueueWorker hosted service.
    /// Call RegisterJob&lt;TJob, TWorker&gt; after this to register specific job types.
    /// </summary>
    public static IServiceCollection AddJobOrchestration(
        this IServiceCollection services,
        TimeSpan? defaultDebounce = null)
    {
        services.AddSingleton<JobTypeRegistry>();

        services.AddSingleton<IQueue<JobEnvelope>>(sp =>
            new InMemoryQueue<JobEnvelope>(new InMemoryQueueOptions<JobEnvelope>
            {
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>()
            }));

        services.AddSingleton<IJobOrchestrator>(sp =>
            new JobOrchestrator(
                sp.GetRequiredService<IQueue<JobEnvelope>>(),
                sp.GetRequiredService<JobTypeRegistry>(),
                defaultDebounce ?? TimeSpan.FromMilliseconds(300),
                sp.GetRequiredService<ILogger<JobOrchestrator>>()));

        services.AddHostedService<JobQueueWorker>();

        return services;
    }

    /// <summary>
    /// Registers a job type and its worker. The job type name is used as the keyed service key.
    /// Optionally sets the default debounce window for this job type (overrides AddJobOrchestration default).
    /// </summary>
    public static IServiceCollection RegisterJob<TJob, TWorker>(
        this IServiceCollection services,
        TimeSpan? debounce = null)
        where TJob : class, IJob
        where TWorker : class, IJobWorker<TJob>
    {
        services.AddKeyedScoped<IJobWorker, TWorker>(typeof(TJob).Name);

        // Defer registry population until after the container is built
        services.AddSingleton<IJobRegistration>(new JobRegistration<TJob>(debounce));

        return services;
    }

    /// <summary>
    /// Applies all pending IJobRegistration entries to the JobTypeRegistry.
    /// Called once from Program.cs after building the app, before starting it.
    /// </summary>
    public static IServiceProvider ApplyJobRegistrations(this IServiceProvider services)
    {
        var registry = services.GetRequiredService<JobTypeRegistry>();
        foreach (var reg in services.GetServices<IJobRegistration>())
            reg.Apply(registry);
        return services;
    }
}

// ── Internal helpers ─────────────────────────────────────────────────────────

public interface IJobRegistration { void Apply(JobTypeRegistry registry); }

file sealed class JobRegistration<TJob>(TimeSpan? debounce) : IJobRegistration where TJob : IJob
{
    public void Apply(JobTypeRegistry registry) => registry.Register<TJob>(debounce);
}
