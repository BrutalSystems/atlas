using Atlas.Helpers;
using Foundatio.Caching;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Atlas.Email.Models;
using Atlas.Email.Settings;

namespace Atlas.Email.Providers;

//todo:  implement connection caching at some point

/// <summary>
/// IMAP implementation of the IMailProvider interface using MailKit.
/// </summary>
public class ImapMailProvider : IMailProvider
{
    private readonly ILogger<ImapMailProvider> _logger = Logging.CreateLogger<ImapMailProvider>();
    private readonly ICacheClient _cacheClient = new InMemoryCacheClient();
    private readonly ImapCallExecutor _imapExec = new ImapCallExecutor(Logging.CreateLogger<ImapCallExecutor>());
    private ImapClient? _imapClient = null;
    private readonly ImapSettings imapSettings;

    public ImapMailProvider(MailSettings mailSettings)
    {
        if (mailSettings is not ImapSettings settings)
        {
            throw new ArgumentException("Invalid settings type. Expected ImapSettings.", nameof(mailSettings));
        }

        imapSettings = settings;
    }

    private async Task<ImapClient> GetImapClient(CancellationToken cancellationToken = default)
    {
        if (_imapClient == null)
        {
            _imapClient = new ImapClient();
        }

        var secureSocketOptions = imapSettings.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
        if (!_imapClient.IsConnected)
        {
            await _imapClient.ConnectAsync(imapSettings.Server, imapSettings.Port, secureSocketOptions, cancellationToken);
        }

        if (!_imapClient.IsAuthenticated)
        {
            await _imapClient.AuthenticateAsync(imapSettings.Username, imapSettings.Password, cancellationToken);
        }

        return _imapClient;

        //todo: need to dispose
    }


    public async Task<IEnumerable<MailMessage>> FetchMessagesAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default)
    {
        return await _imapExec.ExecuteAsync(
            provider: "imap",
            operation: "Imap.FetchMessages",
            limiterGroup: "imap",
            accountKey: imapSettings.Username,
            maxConcurrency: 1,
            opAsync: async ct =>
            {
                var client = await this.GetImapClient(ct);

                var folderToOpen = string.IsNullOrEmpty(request.Folder) ? client.Inbox : client.GetFolder(request.Folder);
                await folderToOpen.OpenAsync(FolderAccess.ReadWrite, ct);

                SearchQuery query = SearchQuery.DeliveredAfter(request.Since.DateTime);
                if (request.Until.HasValue)
                {
                    query = query.And(SearchQuery.DeliveredBefore(request.Until.Value.DateTime));
                }

                var uids = await folderToOpen.SearchAsync(query, ct);

                var limitedUids = uids.Take(request.MaxCount).ToList();

                _logger.LogDebug("Found {Count} messages since {Since} for {Username}@{Server}", limitedUids.Count, request.Since, imapSettings.Username, imapSettings.Server);

                var messages = new List<MailMessage>();
                var totalFetched = 0;
                int processedMessages = 0;
                int totalMessages = await this.FetchMessagesCountAsync(request, ct);

                if (limitedUids.Count > 0)
                {
                    var batchSize = Math.Min(50, limitedUids.Count);
                    for (int i = 0; i < limitedUids.Count && messages.Count < request.MaxCount; i += batchSize)
                    {
                        var batch = limitedUids.Skip(i).Take(batchSize).ToList();
                        var batchMessages = await folderToOpen.FetchAsync(batch, MessageSummaryItems.All | MessageSummaryItems.GMailThreadId, ct);

                        foreach (var summary in batchMessages)
                        {
                            var mailMessage = ConvertToMailMessage(summary);
                            mailMessage.Folders.Add(new EmailFolder
                            {
                                Id = folderToOpen.FullName,
                                Name = folderToOpen.Name,
                                ProviderFolder = folderToOpen
                            });
                            totalFetched++;

                            if (request.Filter != null)
                            {
                                if (await request.Filter(mailMessage, processedMessages++, totalMessages))
                                {
                                    messages.Add(mailMessage);
                                }
                            }
                            else
                            {
                                messages.Add(mailMessage);
                            }

                            if (messages.Count >= request.MaxCount)
                            {
                                break;
                            }
                        }
                    }
                }

                _logger.LogInformation("Successfully fetched {Count} messages for {Username}@{Server}", messages.Count, imapSettings.Username, imapSettings.Server);

                return messages;
            },
            reconnectAsync: ct => this.GetImapClient(ct),
            allowRetry: true,
            cancellationToken: cancellationToken);
    }

    public async Task<int> FetchMessagesCountAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default)
    {
        return 0;
    }

    public async Task MoveMessageAsync(MailMessage emessage, string folderName, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        var (mustExist, removeFromOthers, parsedFolderName) = ParseFolderFlags(folderName);
        try
        {
            await _imapExec.ExecuteAsync(
                provider: "imap",
                operation: "Imap.MoveMessage",
                limiterGroup: "imap",
                accountKey: imapSettings.Username,
                maxConcurrency: 1,
                opAsync: async ct =>
                {
                    var client = await this.GetImapClient(ct);
                    var uid = this.GetMessageUniqueId(messageId);
                    var sourceFolder = emessage.Folders.FirstOrDefault()?.ProviderFolder as IMailFolder ?? await FindMessageAsync(client, messageId, ct);
                    if (sourceFolder == null)
                    {
                        throw new InvalidOperationException($"Message with ID {messageId} not found");
                    }

                    var destinationFolder = await GetOrCreateFolderAsync(client, parsedFolderName, !mustExist, ct);
                    if (destinationFolder == null)
                    {
                        throw new InvalidOperationException($"Destination folder {parsedFolderName} not found");
                    }

                    await sourceFolder.MoveToAsync(uid, destinationFolder, ct);
                    return true;
                },
                reconnectAsync: null,
                allowRetry: false,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully moved message {MessageId} to folder {Folder} for {Username}@{Server}", messageId, folderName, imapSettings.Username, imapSettings.Server);
        }
        catch (Exception ex)
        {
            if (!mustExist)
                _logger.LogError(ex, "Failed to move message {MessageId} to folder {Folder} for {Username}@{Server}", messageId, folderName, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    public async Task DeleteMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            await _imapExec.ExecuteAsync(
                provider: "imap",
                operation: "Imap.DeleteMessage",
                limiterGroup: "imap",
                accountKey: imapSettings.Username,
                maxConcurrency: 1,
                opAsync: async ct =>
                {
                    var client = await this.GetImapClient(ct);
                    var uid = this.GetMessageUniqueId(messageId);
                    var folder = emessage.Folders.FirstOrDefault()?.ProviderFolder as IMailFolder ?? await FindMessageAsync(client, messageId, ct);
                    if (folder == null)
                    {
                        throw new InvalidOperationException($"Message with ID {messageId} not found");
                    }

                    await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, ct);
                    return true;
                },
                reconnectAsync: null,
                allowRetry: false,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted message {MessageId} for {Username}@{Server}", messageId, imapSettings.Username, imapSettings.Server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} for {Username}@{Server}", messageId, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    public async Task MarkAsReadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await ModifyMessageFlagsAsync(emessage, MessageFlags.Seen, true, cancellationToken);
    }

    public async Task MarkAsUnreadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await ModifyMessageFlagsAsync(emessage, MessageFlags.Seen, false, cancellationToken);
    }

    public async Task ArchiveMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await MoveMessageAsync(emessage, "Archive", cancellationToken);
    }

    public async Task AddFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        await ModifyMessageKeywordsAsync(emessage, new[] { flag }, true, cancellationToken);
    }

    public async Task RemoveFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        await ModifyMessageKeywordsAsync(emessage, new[] { flag }, false, cancellationToken);
    }

    public async Task<IEnumerable<string>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var folderMap = await GetCachedFoldersAsync(cancellationToken);
            var folderNames = folderMap.Keys.ToList();

            _logger.LogDebug("Retrieved {Count} folders for {Username}@{Server}", folderNames.Count, imapSettings.Username, imapSettings.Server);

            return folderNames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folders for {Username}@{Server}", imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    public async Task<string?> GetFolderIdAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);

            var folderMap = await GetCachedFoldersAsync(cancellationToken);

            if (folderMap.TryGetValue(folderName, out var folder))
            {
                var folderId = folder.FullName;
                _logger.LogDebug("Retrieved folder ID {FolderId} for folder {FolderName} for {Username}@{Server}", folderId, folderName, imapSettings.Username, imapSettings.Server);
                return folderId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    public async Task<string> CreateFolderAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);

            var folderId = await _imapExec.ExecuteAsync(
                provider: "imap",
                operation: "Imap.CreateFolder",
                limiterGroup: "imap",
                accountKey: imapSettings.Username,
                maxConcurrency: 1,
                opAsync: async ct =>
                {
                    var client = await this.GetImapClient(ct);
                    var folder = await this.GetOrCreateFolderAsync(client, folderName, true, ct);
                    return folder.FullName;
                },
                reconnectAsync: null,
                allowRetry: false,
                cancellationToken: cancellationToken);

            var cacheKey = $"FolderMap:{imapSettings.Username}@{imapSettings.Server}";
            await _cacheClient.RemoveAsync(cacheKey);

            _logger.LogInformation("Successfully created folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);

            return folderId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    public Task<MailSettings> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(imapSettings as MailSettings);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await this.GetImapClient(cancellationToken);

            _logger.LogDebug("Testing IMAP connection to {Server}:{Port}", imapSettings.Server, imapSettings.Port);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            _logger.LogInformation("IMAP connection test successful for {Username}@{Server}", imapSettings.Username, imapSettings.Server);

            return new ConnectionTestResult
            {
                Success = true,
                Details = $"Successfully connected to {imapSettings.Server}:{imapSettings.Port}",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAP connection test failed for {Username}@{Server}", imapSettings.Username, imapSettings.Server);

            return new ConnectionTestResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Details = ex.ToString(),
            };
        }
    }

    // ----- Private Helper Methods

    private (bool mustExist, bool removeFromOthers, string folderName) ParseFolderFlags(string folderName)
    {
        bool mustExist = false;
        bool removeFromOthers = false;

        while (folderName.Length > 0 && (folderName.EndsWith('!') || folderName.EndsWith('#')))
        {
            var lastChar = folderName[^1];
            if (lastChar == '!')
            {
                mustExist = true;
            }
            else if (lastChar == '#')
            {
                removeFromOthers = true;
            }

            folderName = folderName[..^1];
        }

        return (mustExist, removeFromOthers, folderName);
    }

    private async Task<Dictionary<string, IMailFolder>> GetCachedFoldersAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"FolderMap:{imapSettings.Username}@{imapSettings.Server}";
        var cachedFolders = (await _cacheClient.GetAsync<Dictionary<string, IMailFolder>>(cacheKey)).Value;

        if (cachedFolders != null)
        {
            _logger.LogDebug("Retrieved {Count} cached folders for {Username}@{Server}", cachedFolders.Count, imapSettings.Username, imapSettings.Server);
            return cachedFolders;
        }

        var client = await this.GetImapClient(cancellationToken);

        var folders = await client.GetFoldersAsync(client.PersonalNamespaces[0], cancellationToken: cancellationToken);
        var folderMap = new Dictionary<string, IMailFolder>();

        foreach (var folder in folders)
        {
            if (!string.IsNullOrEmpty(folder.FullName) && folder.CanOpen)
            {
                folderMap[folder.FullName] = folder;
            }
        }

        await _cacheClient.SetAsync(cacheKey, folderMap, TimeSpan.FromMinutes(10));

        _logger.LogDebug("Fetched and cached {Count} folders for {Username}@{Server}", folderMap.Count, imapSettings.Username, imapSettings.Server);

        return folderMap;
    }

    private async Task<IMailFolder?> GetFolderAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            var folderMap = await GetCachedFoldersAsync(cancellationToken);

            if (folderMap.TryGetValue(folderName, out var folder))
            {
                return folder;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folder {FolderName} for {Username}@{Server}", folderName, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    private MailMessage ConvertToMailMessage(IMessageSummary summary)
    {
        var threadIdAsHex = summary.GMailThreadId == null ? null : summary.GMailThreadId.GetValueOrDefault().ToString("x");
        var gmailUrl = threadIdAsHex == null ? null : $"https://mail.google.com/mail/u/0/#inbox/{threadIdAsHex}";

        var fromMailbox = summary.Envelope.From.Mailboxes.FirstOrDefault();
        var fromName = fromMailbox?.Name ?? string.Empty;
        var fromEmail = fromMailbox?.Address ?? string.Empty;
        var fromDomain = fromEmail.Contains('@') ? fromEmail.Split('@')[1] : string.Empty;

        var mailMessage = new MailMessage
        {
            MessageId = summary.UniqueId.ToString(),
            From = summary.Envelope.From.FirstOrDefault()?.ToString() ?? string.Empty,
            FromName = fromName,
            FromEmail = fromEmail,
            FromDomain = fromDomain,
            To = summary.Envelope.To.Select(addr => addr.ToString()).ToList(),
            Subject = summary.Envelope.Subject ?? string.Empty,
            ReceivedDate = summary.Envelope.Date ?? DateTimeOffset.MinValue,
            IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
            HasAttachments = summary.Attachments?.Any() ?? false,
            Flags = ConvertFlags(summary.Flags, summary.Keywords != null ? new HashSet<string>(summary.Keywords) : null),
            SourceLink = gmailUrl,
        };

        if (summary.Envelope.From.Count() > 1)
        {
            _logger.LogWarning("Message {MessageId} has multiple From addresses", summary.UniqueId);
        }

        return mailMessage;
    }

    private List<string> ConvertFlags(MessageFlags? flags, HashSet<string>? keywords)
    {
        var result = new List<string>();

        if (flags.HasValue)
        {
            if (flags.Value.HasFlag(MessageFlags.Seen))
                result.Add("Seen");
            if (flags.Value.HasFlag(MessageFlags.Answered))
                result.Add("Answered");
            if (flags.Value.HasFlag(MessageFlags.Flagged))
                result.Add("Flagged");
            if (flags.Value.HasFlag(MessageFlags.Deleted))
                result.Add("Deleted");
            if (flags.Value.HasFlag(MessageFlags.Draft))
                result.Add("Draft");
        }

        if (keywords != null)
        {
            result.AddRange(keywords);
        }

        return result;
    }

    private UniqueId GetMessageUniqueId(string messageId)
    {
        if (uint.TryParse(messageId, out var uidValue))
        {
            return new UniqueId(uidValue);
        }
        else
        {
            throw new ArgumentException($"Invalid message ID format: {messageId}", nameof(messageId));
        }
    }

    private async Task<IMailFolder?> FindMessageAsync(ImapClient client, string messageId, CancellationToken cancellationToken)
    {
        var uid = this.GetMessageUniqueId(messageId);
        // if (uid == null)
        // {
        //     return null;
        // }

        return null;
    }

    private async Task<IMailFolder> GetOrCreateFolderAsync(ImapClient client, string folderName, bool shouldCreate, CancellationToken cancellationToken)
    {
        try
        {
            var result = await this.GetFolderAsync(folderName, cancellationToken);
            if (result == null && shouldCreate)
            {
                var personalNamespace = client.PersonalNamespaces.FirstOrDefault();
                if (personalNamespace != null)
                {
                    var parentFolder = client.GetFolder(personalNamespace);
                    var newFolder = parentFolder;
                    foreach (var folderNamePart in folderName.Split('/'))
                    {
                        newFolder = await newFolder.CreateAsync(folderNamePart, true, cancellationToken);
                    }

                    return newFolder;
                }
            }

            return result ?? throw new InvalidOperationException($"Folder {folderName} not found");
        }
        catch
        {
            throw;
        }
    }

    private async Task ModifyMessageFlagsAsync(MailMessage emessage, MessageFlags flag, bool addFlag, CancellationToken cancellationToken)
    {
        string messageId = emessage.MessageId;
        try
        {
            await _imapExec.ExecuteAsync(
                provider: "imap",
                operation: "Imap.ModifyFlags",
                limiterGroup: "imap",
                accountKey: imapSettings.Username,
                maxConcurrency: 1,
                opAsync: async ct =>
                {
                    var client = await this.GetImapClient(ct);

                    var uid = this.GetMessageUniqueId(messageId);
                    var folder = emessage.Folders.FirstOrDefault()?.ProviderFolder as IMailFolder ?? await FindMessageAsync(client, messageId, ct);
                    if (folder == null)
                    {
                        throw new InvalidOperationException($"Message with ID {messageId} not found");
                    }

                    if (addFlag)
                    {
                        await folder.AddFlagsAsync(uid, flag, true, ct);
                    }
                    else
                    {
                        await folder.RemoveFlagsAsync(uid, flag, true, ct);
                    }

                    return true;
                },
                reconnectAsync: null,
                allowRetry: false,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully modified flag {Flag} ({Action}) for message {MessageId} for {Username}@{Server}", flag, addFlag ? "add" : "remove", messageId, imapSettings.Username, imapSettings.Server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify flag {Flag} for message {MessageId} for {Username}@{Server}", flag, messageId, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }

    private async Task ModifyMessageKeywordsAsync(MailMessage emessage, IList<string> keywords, bool addKeywords, CancellationToken cancellationToken)
    {
        string messageId = emessage.MessageId;
        try
        {
            await _imapExec.ExecuteAsync(
                provider: "imap",
                operation: "Imap.ModifyKeywords",
                limiterGroup: "imap",
                accountKey: imapSettings.Username,
                maxConcurrency: 1,
                opAsync: async ct =>
                {
                    var client = await this.GetImapClient(ct);
                    var uid = this.GetMessageUniqueId(messageId);
                    var folder = emessage.Folders.FirstOrDefault()?.ProviderFolder as IMailFolder ?? await FindMessageAsync(client, messageId, ct);
                    if (folder == null)
                    {
                        throw new InvalidOperationException($"Message with ID {messageId} not found");
                    }

                    if (addKeywords)
                    {
                        await folder.AddLabelsAsync(uid, keywords, true, ct);
                    }
                    else
                    {
                        await folder.RemoveLabelsAsync(uid, keywords, true, ct);
                    }

                    return true;
                },
                reconnectAsync: null,
                allowRetry: false,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully modified keywords {Keywords} ({Action}) for message {MessageId} for {Username}@{Server}", string.Join(", ", keywords), addKeywords ? "add" : "remove", messageId, imapSettings.Username, imapSettings.Server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify keywords for message {MessageId} for {Username}@{Server}", messageId, imapSettings.Username, imapSettings.Server);
            throw;
        }
    }
}
