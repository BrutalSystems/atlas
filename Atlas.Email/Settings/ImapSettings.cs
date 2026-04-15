namespace Atlas.Email.Settings;

/// <summary>
/// IMAP-specific settings.
/// </summary>
public class ImapSettings : MailSettings
{
    /// <summary>
    /// Gets or sets the IMAP server hostname.
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the IMAP server port.
    /// </summary>
    public int Port { get; set; } = 993;

    /// <summary>
    /// Gets or sets whether to use SSL/TLS encryption.
    /// </summary>
    public bool UseSsl { get; set; } = true;
}
