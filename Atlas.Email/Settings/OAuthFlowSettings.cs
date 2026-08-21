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
    /// Scheme+authority values a returnUrl may point at, e.g. "https://sift.brutalsystems.com".
    /// EMPTY REJECTS EVERYTHING -- fail closed, never open.
    /// </summary>
    public string[] AllowedReturnOrigins { get; set; } = [];

    /// <summary>How long an unconsumed flow stays valid.</summary>
    public int StateTtlMinutes { get; set; } = 10;
}
