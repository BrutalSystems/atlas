namespace Atlas.Services.Jobs;

/// <summary>
/// Controls how urgently a job is processed relative to the debounce window.
/// </summary>
public enum JobUrgency
{
    /// <summary>
    /// Default. Debounce applies if the job has a DebounceKey — rapid re-enqueues
    /// coalesce into a single execution after the debounce window elapses.
    /// </summary>
    Normal,

    /// <summary>
    /// Skip the debounce window and publish to the transport immediately.
    /// Useful when a caller needs the side-effects of the job visible before continuing
    /// (e.g. a synchronous HTTP response that depends on the job's output).
    /// </summary>
    Immediate,
}
