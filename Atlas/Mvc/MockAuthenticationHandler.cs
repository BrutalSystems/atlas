using System.Security.Claims;
using Atlas.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Atlas.Mvc;

public class MockAuthenticationHandler(
    IOptionsMonitor<MockAuthenticationOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    AuthSettings authSettings)
    : AuthenticationHandler<MockAuthenticationOptions>(options,
        logger, encoder)
{
    public const string AuthenticationScheme = "Mock";
    private AuthSettings AuthSettings { get; } = authSettings;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        if (endpoint == null || endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>()
        {
            new System.Security.Claims.Claim("aud", ""),
            new System.Security.Claims.Claim("client_id", ""),
            new System.Security.Claims.Claim("sub", this.AuthSettings.MockUserId),
            new System.Security.Claims.Claim("name", this.AuthSettings.MockUserName),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, this.AuthSettings.MockUserEmail),
            new System.Security.Claims.Claim(this.AuthSettings.TenantIdClaim, this.AuthSettings.MockTenantId),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Custom"));
        var ticket = new AuthenticationTicket(principal, this.Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class MockAuthenticationOptions : AuthenticationSchemeOptions
{
}