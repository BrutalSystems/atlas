using System.Net;
using Atlas.Mvc;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Atlas.Tests.Mvc;

public class UserContextResolveClientIpTests
{
    // We call the internal static method directly. ResolveClientIp is exposed to this
    // assembly via [InternalsVisibleTo("Atlas.Tests")] in Atlas/Atlas.csproj — that keeps
    // the test off the UserContext ctor's Env.GetConfiguration() path, which is brittle
    // in a unit-test process.
    private static IPAddress? Resolve(HttpContext ctx) => UserContext.ResolveClientIp(ctx, null);

    private static DefaultHttpContext Ctx(string? remote, string? xff = null, string? realIp = null)
    {
        var ctx = new DefaultHttpContext();
        if (remote != null) ctx.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (xff != null) ctx.Request.Headers["X-Forwarded-For"] = xff;
        if (realIp != null) ctx.Request.Headers["X-Real-IP"] = realIp;
        return ctx;
    }

    [Fact]
    public void Prefers_RemoteIpAddress_when_middleware_has_already_resolved_it()
    {
        var ctx = Ctx(remote: "203.0.113.7", xff: "10.0.0.5", realIp: "10.0.0.5");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }

    [Fact]
    public void Falls_back_to_first_XFF_entry_when_RemoteIpAddress_is_loopback()
    {
        var ctx = Ctx(remote: "127.0.0.1", xff: "203.0.113.7, 10.0.0.5, 10.0.0.6");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }

    [Fact]
    public void Falls_back_to_X_Real_IP_when_no_XFF_and_RemoteIpAddress_is_loopback()
    {
        var ctx = Ctx(remote: "127.0.0.1", realIp: "203.0.113.7");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }

    [Fact]
    public void Returns_RemoteIpAddress_unchanged_when_no_headers_present()
    {
        var ctx = Ctx(remote: "203.0.113.7");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }

    [Fact]
    public void Maps_IPv4_mapped_IPv6_to_plain_IPv4()
    {
        var ctx = Ctx(remote: "::ffff:203.0.113.7");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }

    [Fact]
    public void Ignores_malformed_XFF_entries_and_keeps_walking()
    {
        var ctx = Ctx(remote: "127.0.0.1", xff: "not-an-ip, 203.0.113.7, 10.0.0.5");
        Assert.Equal(IPAddress.Parse("203.0.113.7"), Resolve(ctx));
    }
}
