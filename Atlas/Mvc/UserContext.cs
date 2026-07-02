using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Atlas.Extensions;
using Atlas.Helpers;
using Atlas.Services.Jobs;
using Atlas.Settings;
using Foundatio.Caching;
using Microsoft.Extensions.Logging;

namespace Atlas.Mvc;

public class UserContext
{
    private List<System.Security.Claims.Claim>? Claims { get; set; }
    public string? Database { get; private set; }
    public string? Audience { get; private set; }
    public string? ClientId { get; private set; }
    public string? AuthUserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string? UserName { get; private set; }
    public string? TenantId { get; internal set; }
    public bool IsMockUser { get; private set; } = false;
    public string? Issuer { get; private set; }    
    public bool IsAuthenticated { get; } = false;
    public string? ConnectionString { get; set; }
    public ICacheClient? CacheClient { get; private set; }
    public IHeaderDictionary? Headers { get; set; }

    public System.Net.IPAddress? ClientIp { get; private set; }

    public UserContext(AuthSettings? authSettings = null, HttpContext? httpContext = null, IHttpContextAccessor? httpContextAccessor = null, ICacheClient? cacheClient = null, ILogger<UserContext>? logger = null)
    {
        CacheClient = cacheClient ?? new InMemoryCacheClient(); //todo:  is this ok?   useful for unit tests
        var configuration = Env.GetConfiguration();
        authSettings ??= configuration.GetSettings<AuthSettings>() ?? new AuthSettings();
        httpContext ??= httpContextAccessor?.HttpContext;

        if (httpContext != null)
        {
            this.Headers = httpContext.Request.Headers;
            this.ClientIp = ResolveClientIp(httpContext, logger);
        }
        else if (httpContext == null)
        {
            if (authSettings.UseMockAuthentication)
            {
                this.AuthUserId = authSettings.MockUserId;
                this.UserEmail = authSettings.MockUserEmail;
                this.UserName = authSettings.MockUserName;
                this.TenantId = authSettings.MockTenantId;
                this.IsMockUser = true;
            }
            return;
        }

        this.Claims = httpContext.User.Claims.ToList();
        var tenantIdHeader = httpContext!.Request.Headers[authSettings.TenantIdClaim];
        var tenantIdString = authSettings.UseMockAuthentication ? "" : tenantIdHeader.ToString();
        if (tenantIdString.IsNullOrWhiteSpace())
            tenantIdString = this.Claims.FirstOrDefault(x => x.Type == authSettings.TenantIdClaim)?.Value;

        var isAuthenticated = this.IsAuthenticated = httpContext?.User?.Identity?.IsAuthenticated ?? false;
        if (isAuthenticated == false && cacheClient != null && httpContext!.Request.Query.Count > 0)
        {
            foreach (var name in authSettings.AnonymousCallbackParam)
            {
                var val = httpContext.Request.Query[name].ToString();
                if (val.IsNullOrWhiteSpace())
                    continue;

                AnonymousCallbackLink? anonLink = null;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    anonLink = (await cacheClient.GetAsync<AnonymousCallbackLink>(val)).Value;
                }).Wait();

                if (anonLink != null)
                {
                    tenantIdString = anonLink.TenantId;
                    
                    break;
                }
            }
        }

        if (tenantIdString == "(null)" || tenantIdString == null)
            this.TenantId = null;
        else if (!tenantIdString.IsNullOrWhiteSpace())
            this.TenantId = tenantIdString;

        this.Database = this.Claims.FirstOrDefault(x => x.Type == authSettings.DatabaseClaim)?.Value;
        this.Audience = (string?)this.Claims.FirstOrDefault(x => x.Type == "aud")?.Value;
        this.ClientId = (string?)this.Claims.FirstOrDefault(x => x.Type == "client_id")?.Value;
        this.AuthUserId = httpContext!.User?.Claims
            ?.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")?.Value;

        //todo:  may want settings to indicate email claim key
        this.UserEmail = this.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Email)?.Value
                         ?? (string?)this.Claims.FirstOrDefault(x =>
                             x.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")?.Value;

        this.UserName = this.Claims.FirstOrDefault(x => x.Type == System.Security.Claims.ClaimTypes.Name)?.Value
                        ?? this.Claims.FirstOrDefault(x => x.Type == "name")?.Value
                        ?? this.UserEmail;

        this.IsMockUser = this.UserEmail == authSettings.MockUserEmail && authSettings.UseMockAuthentication;

        // if tenantless authority is configured and issuer matches, set tenant to null to bypass tenant filters        
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (header.StartsWith("bearer ", StringComparison.CurrentCultureIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                this.Issuer = jwt.Issuer;
                if (authSettings.TenantlessAuthority == this.Issuer)
                {
                    this.TenantId = null;
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Creates a UserContext pre-populated from a JobContext.
    /// Used when constructing a context for background job execution outside of an HTTP request.
    /// </summary>
    public UserContext(JobContext jobContext)
    {
        CacheClient = new InMemoryCacheClient();
        this.TenantId = jobContext.TenantId;
        this.AuthUserId = jobContext.AuthUserId;
    }

    /// <summary>
    /// Stamps this UserContext with the identity captured at job enqueue time.
    /// Called by JobQueueWorker on the scoped UserContext before dispatching to the worker,
    /// ensuring EF tenant filters and audit fields work correctly in background execution.
    /// </summary>
    public void SetJobContext(JobContext jobContext)
    {
        this.TenantId = jobContext.TenantId;
        this.AuthUserId = jobContext.AuthUserId;
    }

    /// <summary>
    /// Called by IJobContextEnricher implementations to populate UserEmail and UserName
    /// from a data store lookup after the job context has been stamped.
    /// </summary>
    public void SetUserDetails(string? email, string? name)
    {
        this.UserEmail = email;
        this.UserName = name;
    }

    public void Masquerade(AnonymousCallbackLink acl)
    {
        this.Database = acl.Database ?? this.Database;
        this.TenantId = acl.TenantId ?? this.TenantId;
        this.UserEmail = acl.UserEmail ?? this.UserEmail;
    }

    /// <summary>
    /// Resolves the real client IP.
    /// Priority:
    ///   1. <see cref="ConnectionInfo.RemoteIpAddress"/> if it is a public IP — this is what
    ///      ASP.NET's ForwardedHeadersMiddleware writes after parsing X-Forwarded-For, and is
    ///      the most trustworthy source in production.
    ///   2. Leftmost non-loopback IP in X-Forwarded-For (the original client, per de-facto XFF semantics)
    ///      — used in tests / dev where ForwardedHeadersMiddleware did not run.
    ///   3. X-Real-IP single value — last-resort fallback for upstreams that only set X-Real-IP.
    ///   4. RemoteIpAddress as-is — e.g. loopback in unit tests.
    /// Exposed as internal for direct testing via InternalsVisibleTo("Atlas.Tests").
    /// </summary>
    internal static System.Net.IPAddress? ResolveClientIp(HttpContext httpContext, ILogger<UserContext>? logger = null)
    {
        var remote = httpContext.Connection.RemoteIpAddress;
        var xff = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        var realIpHeader = httpContext.Request.Headers["X-Real-IP"].ToString();

        // logger?.LogDebug("ResolveClientIp — X-Real-IP: '{RealIp}', X-Forwarded-For: '{Xff}', RemoteIpAddress: '{RemoteIp}'",
            // realIpHeader, xff, remote?.ToString());

        System.Net.IPAddress? ip = null;

        // 1. ForwardedHeadersMiddleware result (the post-middleware RemoteIpAddress) — only trust it
        //    if it actually looks like a real client, not loopback/empty.
        if (remote != null && !System.Net.IPAddress.IsLoopback(remote))
            ip = remote;

        // 2. Walk XFF left-to-right and take the first parseable, non-loopback entry.
        if (ip == null && !string.IsNullOrWhiteSpace(xff))
        {
            foreach (var raw in xff.Split(','))
            {
                if (System.Net.IPAddress.TryParse(raw.Trim(), out var parsed) && !System.Net.IPAddress.IsLoopback(parsed))
                {
                    ip = parsed;
                    break;
                }
            }
        }

        // 3. X-Real-IP single header
        if (ip == null && !string.IsNullOrWhiteSpace(realIpHeader)
            && System.Net.IPAddress.TryParse(realIpHeader.Trim(), out var realIp)
            && !System.Net.IPAddress.IsLoopback(realIp))
            ip = realIp;

        // 4. Whatever RemoteIpAddress actually is (including loopback for tests/dev)
        ip ??= remote;

        var resolved = ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4() : ip;
        // logger?.LogDebug("ResolveClientIp — resolved: '{ResolvedIp}'", resolved);

        return resolved;
    }
}