using Atlas.Email.Models;
using Atlas.Email.Settings;

namespace Atlas.Email;

/// <summary>
/// Abstraction for mail provider services (IMAP, Gmail API, Outlook API, etc.).
/// </summary>
public interface IMailProvider
{
    Task<IEnumerable<MailMessage>> FetchMessagesAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default);

    Task MoveMessageAsync(MailMessage message, string folderName, CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(MailMessage message, CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(MailMessage message, CancellationToken cancellationToken = default);

    Task MarkAsUnreadAsync(MailMessage message, CancellationToken cancellationToken = default);

    Task ArchiveMessageAsync(MailMessage message, CancellationToken cancellationToken = default);

    Task AddFlagAsync(MailMessage message, string flag, CancellationToken cancellationToken = default);

    Task RemoveFlagAsync(MailMessage message, string flag, CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetFoldersAsync(CancellationToken cancellationToken = default);

    Task<string?> GetFolderIdAsync(string folderName, CancellationToken cancellationToken = default);

    Task<string> CreateFolderAsync(string folderName, CancellationToken cancellationToken = default);

    Task<MailFolderStats> GetFolderStatsAsync(string folderName, CancellationToken cancellationToken = default);

    Task<MailFolderStats> GetRecentFolderStatsAsync(string folderName, int days, string? timeZoneId = null, CancellationToken cancellationToken = default);

    Task<MailSettings> RefreshTokenAsync(CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The files attached to a message, inline ones included (see <see cref="MailAttachment.IsInline"/>). IMAP needs the
    /// message's folder in <c>message.Folders</c>; Gmail and Outlook find the message by id alone. Providers that don't
    /// support attachments throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<IReadOnlyList<MailAttachment>> GetAttachmentsAsync(MailMessage message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} can't read attachments.");

    /// <summary>
    /// One attachment's bytes, by the <see cref="MailAttachment.Id"/> <see cref="GetAttachmentsAsync"/> gave, or null when
    /// the message has no such attachment.
    /// </summary>
    Task<byte[]?> GetAttachmentContentAsync(MailMessage message, string attachmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException($"{GetType().Name} can't read attachments.");
}
