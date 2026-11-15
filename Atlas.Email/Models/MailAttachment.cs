namespace Atlas.Email.Models;

/// <summary>
/// A file attached to a mail message, without its bytes (<see cref="IMailProvider.GetAttachmentContentAsync"/> gets those).
/// </summary>
public class MailAttachment
{
    /// <summary>
    /// The provider's id for the attachment within its message, stable across calls: the MIME part id for Gmail and
    /// IMAP (Gmail's own attachmentId changes on every fetch), the attachment id for Outlook.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Size in bytes as the provider reports it (for IMAP, the encoded size, so a little larger).</summary>
    public long Size { get; set; }

    /// <summary>Shown in the body (a signature logo, a pasted screenshot) rather than attached as a file.</summary>
    public bool IsInline { get; set; }
}
