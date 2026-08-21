using System.Security.Cryptography;

namespace Atlas.Email.Security;

/// <summary>
/// Generates the value carried in the OAuth `state` parameter.
///
/// This replaced Ulid.New(). ByteAether.Ulid is monotonic within a millisecond -- three values
/// minted in succession differ by a single increment in the final character -- so anyone holding
/// one state value could derive its neighbours. The per-millisecond seed is CSPRNG, but the
/// increment destroys that for everything after it.
/// </summary>
internal static class OAuthStateToken
{
    /// <summary>256 bits from a CSPRNG, base64url, unpadded.</summary>
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);

        // Unpadded deliberately: '=' is not a legal character in a cookie name, and the deferred
        // nonce-cookie work may key a cookie on this token.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
