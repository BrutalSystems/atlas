namespace Atlas.Email.Settings;

/// <summary>
/// Gmail API-specific settings.
/// </summary>
public class GmailApiSettings : MailSettings
{
    /// <summary>
    /// Gets or sets the OAuth2 client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token expiry date.
    /// </summary>
    public DateTime? TokenExpiry { get; set; }
}
