namespace Atlas.Email.Security;

/// <summary>
/// Validates the caller-supplied returnUrl before it is stored on the flow state.
///
/// It is validated at WRITE time, on an authenticated request that can reject cleanly, rather
/// than at read time on an anonymous callback whose only recourse is an error page. Storing a
/// value we already vetted makes the callback's redirect target trusted by construction.
///
/// Origin is the whole security boundary -- a redirect to our own origin is not an open redirect
/// whatever its path -- so the path is deliberately not constrained.
/// </summary>
internal static class OAuthReturnUrlValidator
{
    public static bool IsAllowed(string? returnUrl, IReadOnlyCollection<string> allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return false;

        // An unconfigured deployment rejects everything. Never treat "nothing configured" as
        // "anything goes"; see the startup warning in Sift.Api/Program.cs.
        if (allowedOrigins is null || allowedOrigins.Count == 0)
            return false;

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
            return false;

        // Compare scheme+authority, never a string prefix: "https://sift.example.com.evil.com"
        // starts with an allowed origin but is a different site.
        var origin = $"{uri.Scheme}://{uri.Authority}";

        foreach (var allowed in allowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            if (!Uri.TryCreate(allowed.TrimEnd('/'), UriKind.Absolute, out var allowedUri))
                continue;

            var allowedOrigin = $"{allowedUri.Scheme}://{allowedUri.Authority}";
            if (string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
