using Atlas.Email.Security;
using Xunit;

namespace Atlas.Tests.Email;

/// <summary>
/// The state token is the value that travels in the OAuth `state` parameter. It replaced
/// Ulid.New(), which is monotonic within a millisecond -- consecutive ULIDs differ by a single
/// increment, so anyone holding one could derive its neighbours.
/// </summary>
public class OAuthStateTokenTests
{
    [Fact]
    public void Produces_url_safe_unpadded_base64()
    {
        var token = OAuthStateToken.New();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);   // '=' is not legal in a cookie name; keep it out now
    }

    [Fact]
    public void Encodes_256_bits()
    {
        // 32 bytes -> 43 base64 chars once padding is stripped.
        Assert.Equal(43, OAuthStateToken.New().Length);
    }

    [Fact]
    public void Successive_tokens_share_no_prefix()
    {
        // The ULID failure mode: values minted together differed only in the last character.
        var a = OAuthStateToken.New();
        var b = OAuthStateToken.New();

        Assert.NotEqual(a, b);
        Assert.NotEqual(a[..8], b[..8]);
    }

    [Fact]
    public void Generates_no_duplicates_across_many_calls()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 5_000; i++)
            Assert.True(seen.Add(OAuthStateToken.New()));
    }
}
