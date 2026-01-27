using System.ComponentModel;
using System.Security.Claims;
using Atlas.Extensions;
using Atlas.Helpers;
using Atlas.Settings;
using Foundatio.Caching;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.Auth;

//todo:  review this and make sure its CLEAN

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
    public bool IsAuthenticated { get; } = false;
    public string? ConnectionString { get; set; }
    public ICacheClient? CacheClient { get; private set; }
    public IHeaderDictionary? Headers { get; set; }

    public UserContext(AuthSettings? authSettings = null, HttpContext? httpContext = null, IHttpContextAccessor? httpContextAccessor = null, ICacheClient? cacheClient = null)
    {
        CacheClient = cacheClient ?? new InMemoryCacheClient(); //todo:  is this ok?   useful for unit tests
        var configuration = Env.GetConfiguration();
        authSettings ??= configuration.GetSettings<AuthSettings>() ?? new AuthSettings();
        httpContext ??= httpContextAccessor?.HttpContext;

        if (httpContext != null)
        {
            this.Headers = httpContext.Request.Headers;
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
    }

    public void Masquerade(AnonymousCallbackLink acl)
    {
        this.Database = acl.Database ?? this.Database;
        this.TenantId = acl.TenantId ?? this.TenantId;
        this.UserEmail = acl.UserEmail ?? this.UserEmail;
    }
    // public static UserContext NewMockAuthData(IConfiguration? configuration = null)
    // {
    //     var authSettings = configuration == null ? new AuthSettings() : configuration.GetSettings<AuthSettings>();
    //     return new UserContext(null, null, null, null, authSettings);
    // }
}