namespace Atlas.Email.Abstractions;

/// <summary>
/// One in-flight OAuth authorization, held server-side between authorize-url and callback.
///
/// This is short-lived transactional state, NOT cache data: losing it is a user-visible failure,
/// not a recomputable miss. It previously lived in a per-process ICacheClient, which is why the
/// flow broke on more than one replica.
/// </summary>
public sealed class OAuthFlowState
{
    /// <summary>The value that travels in the OAuth `state` parameter. CSPRNG, never a ULID.</summary>
    public string StateToken { get; set; } = string.Empty;

    /// <summary>"google" or "microsoft" -- so one provider cannot consume the other's state.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Validated when written, therefore trusted when read.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? AuthUserId { get; set; }

    /// <summary>Target account when reconnecting. Only ever set after an ownership check.</summary>
    public string? RowId { get; set; }

    /// <summary>Must be UTC. Npgsql rejects any non-zero offset.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Storage seam for OAuth flow state. Implemented by the consuming application -- Atlas.Email
/// takes no database dependency of its own.
/// </summary>
public interface IOAuthStateStore
{
    Task CreateAsync(OAuthFlowState state, CancellationToken ct = default);

    /// <summary>
    /// Atomically consumes the state. Returns null when it is unknown, expired, already consumed,
    /// or issued for a different provider -- the caller cannot distinguish between them,
    /// deliberately, so no oracle is handed out. Implementations MUST make consumption single-use
    /// under concurrency.
    ///
    /// <paramref name="provider"/> is matched INSIDE the query, not by the caller afterwards.
    /// Checking it after consuming would mean a Microsoft callback presenting a Google token
    /// destroys that token before rejecting it, burning a legitimate in-flight connect.
    /// </summary>
    Task<OAuthFlowState?> TryConsumeAsync(string stateToken, string provider, CancellationToken ct = default);
}
