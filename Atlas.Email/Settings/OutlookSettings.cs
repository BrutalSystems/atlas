namespace Atlas.Email.Settings;

/// <summary>
/// Microsoft Outlook/Graph API settings.
/// </summary>
public class OutlookSettings : MailSettings
{
    /// <summary>
    /// Gets or sets the OAuth2 access token for Microsoft Graph API.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the token expiration time.
    /// </summary>
    public DateTimeOffset? TokenExpiration { get; set; }

    /// <summary>
    /// Gets or sets the Azure AD client ID.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the Azure AD client secret.
    /// </summary>
    public string? ClientSecret { get; set; }
}
