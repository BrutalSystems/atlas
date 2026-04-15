namespace Atlas.Email.Models;

/// <summary>
/// Enumeration of supported mail provider types.
/// </summary>
public enum MailProviderType
{
    /// <summary>IMAP protocol (universal, works with most providers).</summary>
    Imap,

    /// <summary>Gmail API (OAuth2-based, better performance).</summary>
    GmailApi,

    /// <summary>Microsoft Outlook/Graph API.</summary>
    OutlookApi
}
