namespace Atlas.Email.Settings;

/// <summary>
/// Configuration for the OAuth connect flow.
///
/// A NEW class rather than properties on GoogleAppSettings/MicrosoftAppSettings: those live in
/// Atlas CORE, which brokenhip-be, titan-be, orchis-be and yarrow-be all compile against at
/// unpinned HEAD. Keeping this in Atlas.Email keeps the blast radius to sift.
/// </summary>
public class OAuthFlowSettings
{
    /// <summary>
    /// Comma-separated scheme+authority values a returnUrl may point at, e.g.
    /// "https://sift.brutalsystems.com,https://sift.springthroughlabs.com".
    /// EMPTY REJECTS EVERYTHING -- fail closed, never open.
    ///
    /// A single delimited string rather than a string[], deliberately. .NET configuration merges
    /// arrays BY INDEX: a four-entry localhost default in appsettings.json would keep indices 2
    /// and 3 alive when a production configmap sets only 0 and 1, silently allowing localhost
    /// origins in production. A single value is replaced wholesale instead.
    /// </summary>
    public string AllowedReturnOrigins { get; set; } = string.Empty;

    /// <summary>How long an unconsumed flow stays valid.</summary>
    public int StateTtlMinutes { get; set; } = 10;

    /// <summary>
    /// <see cref="AllowedReturnOrigins"/> split and trimmed. Empty when unset, which rejects every
    /// returnUrl -- see the startup warning in Sift.Api/Program.cs.
    /// </summary>
    public IReadOnlyList<string> ParsedReturnOrigins =>
        AllowedReturnOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
