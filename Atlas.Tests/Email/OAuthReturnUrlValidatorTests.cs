using Atlas.Email.Security;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// returnUrl arrives from the caller and is later used as a redirect target after a successful
/// token exchange. Validating the ORIGIN is sufficient: a redirect to our own origin is not an
/// open redirect, whatever the path.
/// </summary>
public class OAuthReturnUrlValidatorTests
{
    private static readonly string[] Allowed =
    [
        "https://sift.brutalsystems.com",
        "https://sift.springthroughlabs.com",
    ];

    [Fact]
    public void Accepts_a_url_on_an_allowed_origin()
    {
        Assert.True(OAuthReturnUrlValidator.IsAllowed(
            "https://sift.brutalsystems.com/sift/oauth-callback", Allowed));
    }

    [Fact]
    public void Accepts_any_path_on_an_allowed_origin()
    {
        Assert.True(OAuthReturnUrlValidator.IsAllowed(
            "https://sift.brutalsystems.com/somewhere/else", Allowed));
    }

    [Fact]
    public void Rejects_a_different_host()
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed(
            "https://evil.example.com/sift/oauth-callback", Allowed));
    }

    [Fact]
    public void Rejects_a_host_that_merely_starts_with_an_allowed_one()
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed(
            "https://sift.brutalsystems.com.evil.example.com/x", Allowed));
    }

    [Fact]
    public void Rejects_a_scheme_downgrade()
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed(
            "http://sift.brutalsystems.com/sift/oauth-callback", Allowed));
    }

    [Fact]
    public void Rejects_a_non_http_scheme()
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed(
            "javascript:alert(1)", Allowed));
    }

    [Fact]
    public void Rejects_a_relative_url()
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed("/sift/oauth-callback", Allowed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_url(string? returnUrl)
    {
        Assert.False(OAuthReturnUrlValidator.IsAllowed(returnUrl, Allowed));
    }

    [Fact]
    public void Fails_closed_when_no_origins_are_configured()
    {
        // An unconfigured deployment rejects everything rather than accepting anything.
        Assert.False(OAuthReturnUrlValidator.IsAllowed(
            "https://sift.brutalsystems.com/sift/oauth-callback", []));
    }

    [Fact]
    public void Ignores_a_trailing_slash_in_configuration()
    {
        Assert.True(OAuthReturnUrlValidator.IsAllowed(
            "https://sift.brutalsystems.com/sift/oauth-callback",
            ["https://sift.brutalsystems.com/"]));
    }
}
