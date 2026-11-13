namespace Atlas.Email.Settings;

/// <summary>
/// The OAuth scopes the Google and Microsoft connect flows ask for: the mail scopes always, and the contacts scopes
/// only when the app asks for the <see cref="Contacts"/> feature (an opt-in per mailbox, so connecting a mailbox
/// stays mail-only). These constants are the only source of truth for the scope lists.
/// </summary>
public static class MailScopes
{
    /// <summary>The feature name the authorize-url endpoints take in <c>features</c>.</summary>
    public const string Contacts = "contacts";

    public const string GoogleMail =
        "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/userinfo.email";

    /// <summary>My contacts, and the Other contacts Gmail saves from mail the person exchanged (People API).</summary>
    public const string GoogleContacts =
        "https://www.googleapis.com/auth/contacts.readonly https://www.googleapis.com/auth/contacts.other.readonly";

    public const string MicrosoftMail = "offline_access User.Read Mail.Read Mail.ReadWrite";

    public const string MicrosoftContacts = "Contacts.Read";

    /// <summary>The features a connect flow may ask for. Unknown names are refused rather than ignored.</summary>
    public static readonly IReadOnlySet<string> KnownFeatures = new HashSet<string>(StringComparer.Ordinal) { Contacts };

    /// <summary>Parses a comma- or space-separated <c>features</c> value; null when it names an unknown feature.</summary>
    public static IReadOnlySet<string>? ParseFeatures(string? features)
    {
        var names = (features ?? "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return names.All(KnownFeatures.Contains) ? names : null;
    }

    public static string Google(bool contacts) => contacts ? $"{GoogleMail} {GoogleContacts}" : GoogleMail;

    public static string Microsoft(bool contacts) => contacts ? $"{MicrosoftMail} {MicrosoftContacts}" : MicrosoftMail;

    /// <summary>Whether the provider granted the contacts scopes (all of them: Google lets a person untick some).</summary>
    public static bool HasContacts(MailSettings? settings) => settings switch
    {
        GmailApiSettings g => HasAll(g.GrantedScopes, GoogleContacts),
        OutlookSettings o => HasAll(o.GrantedScopes, MicrosoftContacts),
        _ => false,
    };

    /// <summary>Microsoft returns Graph scopes both bare ("Contacts.Read") and as URIs
    /// ("https://graph.microsoft.com/Contacts.Read"), and in any case; both count.</summary>
    public static bool HasAll(string? granted, string required)
    {
        var have = (granted ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith("https://graph.microsoft.com/", StringComparison.OrdinalIgnoreCase) ? s["https://graph.microsoft.com/".Length..] : s)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return required.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(have.Contains);
    }
}
