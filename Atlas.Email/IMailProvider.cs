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

    Task<MailSettings> RefreshTokenAsync(CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
