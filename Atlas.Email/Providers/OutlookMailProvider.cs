using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Atlas.Helpers;
using Microsoft.Extensions.Logging;
using Atlas.Email.Models;
using Atlas.Email.Settings;

namespace Atlas.Email.Providers;

/// <summary>
/// Microsoft Outlook/Graph API implementation of the IMailProvider interface.
/// </summary>
public class OutlookMailProvider : IMailProvider
{
    private readonly ILogger<OutlookMailProvider> _logger = Logging.CreateLogger<OutlookMailProvider>();
    private readonly ApiCallExecutor _api = new ApiCallExecutor(Logging.CreateLogger<ApiCallExecutor>());
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly IFolderCacheService _folderCache;
    private const string GraphApiBaseUrl = "https://graph.microsoft.com/v1.0";
    private readonly OutlookSettings outlookSettings;

    public OutlookMailProvider(MailSettings mailSettings, IFolderCacheService folderCache)
    {
        if (mailSettings is not OutlookSettings settings)
        {
            throw new ArgumentException("Invalid settings type. Expected OutlookSettings.", nameof(mailSettings));
        }

        outlookSettings = settings;
        _folderCache = folderCache;
    }

    public async Task<IEnumerable<MailMessage>> FetchMessagesAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching messages since {Since} until {Until} for {Username} (max: {MaxCount})", request.Since, request.Until, outlookSettings.Username, request.MaxCount);

            var messages = new List<MailMessage>();
            const int pageSize = 100;
            var totalFetched = 0;
            int processedMessages = 0;
            int totalMessages = await this.FetchMessagesCountAsync(request);

            var sinceIso = request.Since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var filterQuery = $"receivedDateTime ge {sinceIso}";

            if (request.Until.HasValue)
            {
                var untilIso = request.Until.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
                filterQuery += $" and receivedDateTime le {untilIso}";
            }

            string baseUrl;
            if (!string.IsNullOrEmpty(request.Folder))
            {
                var folderId = await GetFolderIdAsync(request.Folder, cancellationToken);
                if (folderId == null)
                {
                    throw new InvalidOperationException($"Folder '{request.Folder}' not found");
                }

                baseUrl = $"{GraphApiBaseUrl}/me/mailFolders/{folderId}/messages";
            }
            else
            {
                baseUrl = $"{GraphApiBaseUrl}/me/messages";
            }

            var initialUrl = $"{baseUrl}?$top={pageSize}&$filter={filterQuery}&$orderby=receivedDateTime asc";
            string? nextLink = initialUrl;

            do
            {
                var content = await _api.ExecuteHttpAsync(
                    provider: "microsoft",
                    operation: "Graph.Messages.ListPage",
                    limiterGroup: "list",
                    accountKey: outlookSettings.Username,
                    maxConcurrency: 2,
                    sendAsync: ct =>
                    {
                        var httpRequest = new HttpRequestMessage(HttpMethod.Get, nextLink);
                        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                        return _httpClient.SendAsync(httpRequest, ct);
                    },
                    parseAsync: async (response, ct) =>
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        return doc.RootElement.Clone();
                    },
                    ensureSuccess: true,
                    cancellationToken: cancellationToken);

                if (!content.TryGetProperty("value", out var messagesArray) || messagesArray.GetArrayLength() == 0)
                {
                    _logger.LogDebug("No more messages available");
                    break;
                }

                var pageMessages = new List<MailMessage>();
                foreach (var msgElement in messagesArray.EnumerateArray())
                {
                    var message = ConvertToMailMessage(msgElement);
                    if (message != null)
                    {
                        pageMessages.Add(message);
                    }
                }

                totalFetched += pageMessages.Count;

                var filteredMessages = new List<MailMessage>();
                if (request.Filter != null)
                {
                    foreach (var message in pageMessages)
                    {
                        if (await request.Filter(message, processedMessages++, totalMessages))
                        {
                            filteredMessages.Add(message);
                        }
                    }
                }
                else
                {
                    filteredMessages = pageMessages;
                }

                messages.AddRange(filteredMessages);

                _logger.LogDebug("Page fetched: {PageSize} messages, {FilteredCount} passed filter, {TotalCount} total collected (target: {MaxCount})", pageMessages.Count, filteredMessages.Count, messages.Count, request.MaxCount);

                if (messages.Count >= request.MaxCount)
                {
                    _logger.LogDebug("Reached max count of {MaxCount}, stopping pagination", request.MaxCount);
                    break;
                }

                nextLink = content.TryGetProperty("@odata.nextLink", out var nextLinkElement) ? nextLinkElement.GetString() : null;
            } while (!string.IsNullOrEmpty(nextLink));

            var finalMessages = messages.Take(request.MaxCount).ToList();

            _logger.LogInformation("Successfully fetched {Count} messages (from {TotalFetched} total) for {Username}", finalMessages.Count, totalFetched, outlookSettings.Username);

            return finalMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages for {Username}", outlookSettings.Username);
            throw;
        }
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
            string? folderId = await GetFolderIdAsync(parsedFolderName, cancellationToken);
            folderId ??= (mustExist ? null : await CreateFolderAsync(parsedFolderName, cancellationToken));
            if (folderId == null)
            {
                throw new InvalidOperationException($"Destination folder '{parsedFolderName}' does not exist");
            }

            var url = $"{GraphApiBaseUrl}/me/messages/{messageId}/move";
            await _api.ExecuteHttpAsync(
                provider: "microsoft",
                operation: "Graph.Messages.Move",
                limiterGroup: "write",
                accountKey: outlookSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                    request.Content = JsonContent.Create(new { destinationId = folderId });
                    return _httpClient.SendAsync(request, ct);
                },
                parseAsync: (_, _) => Task.FromResult(true),
                ensureSuccess: true,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully moved message {MessageId} to folder {Folder} for {Username}", messageId, folderName, outlookSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move message {MessageId} to folder {Folder} for {Username}", messageId, folderName, outlookSettings.Username);
            throw;
        }
    }

    public async Task DeleteMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var url = $"{GraphApiBaseUrl}/me/messages/{messageId}";
            await _api.ExecuteHttpAsync(
                provider: "microsoft",
                operation: "Graph.Messages.Delete",
                limiterGroup: "write",
                accountKey: outlookSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Delete, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                    return _httpClient.SendAsync(request, ct);
                },
                parseAsync: (_, _) => Task.FromResult(true),
                ensureSuccess: true,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted message {MessageId} for {Username}", messageId, outlookSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} for {Username}", messageId, outlookSettings.Username);
            throw;
        }
    }

    public async Task MarkAsReadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await UpdateMessagePropertyAsync(emessage.MessageId, "isRead", true, cancellationToken);
    }

    public async Task MarkAsUnreadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await UpdateMessagePropertyAsync(emessage.MessageId, "isRead", false, cancellationToken);
    }

    public async Task ArchiveMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        await MoveMessageAsync(emessage, "Archive", cancellationToken);
    }

    public async Task AddFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var currentCategories = await GetMessageCategoriesAsync(messageId, cancellationToken);

            if (!currentCategories.Contains(flag))
            {
                currentCategories.Add(flag);
                await UpdateMessageCategoriesAsync(messageId, currentCategories, cancellationToken);
            }

            _logger.LogDebug("Successfully added flag {Flag} to message {MessageId} for {Username}", flag, messageId, outlookSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add flag {Flag} to message {MessageId} for {Username}", flag, messageId, outlookSettings.Username);
            throw;
        }
    }

    public async Task RemoveFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var currentCategories = await GetMessageCategoriesAsync(messageId, cancellationToken);

            if (currentCategories.Remove(flag))
            {
                await UpdateMessageCategoriesAsync(messageId, currentCategories, cancellationToken);
            }

            _logger.LogDebug("Successfully removed flag {Flag} from message {MessageId} for {Username}", flag, messageId, outlookSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove flag {Flag} from message {MessageId} for {Username}", flag, messageId, outlookSettings.Username);
            throw;
        }
    }

    public async Task<string> CreateFolderAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating folder {FolderName} for {Username}", folderName, outlookSettings.Username);

            if (folderName.Contains('/'))
            {
                var parts = folderName.Split('/');
                var currentPath = "";
                string? parentFolderId = null;

                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";

                    var existingFolderId = await GetFolderIdAsync(currentPath, cancellationToken);
                    if (existingFolderId == null)
                    {
                        parentFolderId = await CreateSingleFolderAsync(parts[i], parentFolderId, cancellationToken);
                        _logger.LogDebug("Created folder {FolderPath} for {Username}", currentPath, outlookSettings.Username);
                    }
                    else
                    {
                        parentFolderId = existingFolderId;
                    }
                }

                var finalFolderId = await GetFolderIdAsync(folderName, cancellationToken);
                if (finalFolderId == null)
                {
                    throw new InvalidOperationException($"Failed to retrieve ID for created folder {folderName}");
                }

                return finalFolderId;
            }
            else
            {
                return await CreateSingleFolderAsync(folderName, null, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create folder {FolderName} for {Username}", folderName, outlookSettings.Username);
            throw;
        }
    }

    private async Task<string> CreateSingleFolderAsync(string folderName, string? parentFolderId, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrEmpty(parentFolderId)
            ? $"{GraphApiBaseUrl}/me/mailFolders"
            : $"{GraphApiBaseUrl}/me/mailFolders/{parentFolderId}/childFolders";

        var folderRequest = new { displayName = folderName };

        var folderId = await _api.ExecuteHttpAsync(
            provider: "microsoft",
            operation: "Graph.MailFolders.Create",
            limiterGroup: "write",
            accountKey: outlookSettings.Username,
            maxConcurrency: 2,
            sendAsync: ct =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                req.Content = JsonContent.Create(folderRequest);
                return _httpClient.SendAsync(req, ct);
            },
            parseAsync: async (response, ct) =>
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                return string.IsNullOrWhiteSpace(id) ? throw new InvalidOperationException("Folder ID not returned from API") : id;
            },
            ensureSuccess: true,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(folderId))
        {
            throw new InvalidOperationException("Folder ID was null");
        }

        await _folderCache.SetFolderIdAsync(outlookSettings.TenantId, outlookSettings.AccountId, folderName, folderId, cancellationToken);

        _logger.LogInformation("Successfully created folder {FolderName} for {Username}", folderName, outlookSettings.Username);

        return folderId;
    }

    public async Task<IEnumerable<string>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var folderMap = await GetCachedFoldersAsync(cancellationToken);
            return folderMap.Keys.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folders for {Username}", outlookSettings.Username);
            throw;
        }
    }

    public async Task<string?> GetFolderIdAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting folder {FolderName} for {Username}", folderName, outlookSettings.Username);

            var folderMap = await GetCachedFoldersAsync(cancellationToken);

            if (folderMap.TryGetValue(folderName, out var folderId))
            {
                return folderId;
            }

            if (folderMap.TryGetValue("*" + folderName, out var altFolderId))
            {
                return altFolderId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folder {FolderName} for {Username}", folderName, outlookSettings.Username);
            throw;
        }
    }

    public async Task<MailSettings> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(outlookSettings.RefreshToken))
        {
            throw new InvalidOperationException("No refresh token available");
        }

        try
        {
            _logger.LogDebug("Refreshing access token for {Username}", outlookSettings.Username);

            var parameters = new Dictionary<string, string>
            {
                { "client_id", outlookSettings.ClientId! },
                { "client_secret", outlookSettings.ClientSecret! },
                { "refresh_token", outlookSettings.RefreshToken },
                { "grant_type", "refresh_token" },
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Token refresh failed for {Username}: {Error}", outlookSettings.Username, error);
                throw new InvalidOperationException($"Token refresh failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<JsonElement>(json);

            outlookSettings.AccessToken = tokenData.GetProperty("access_token").GetString() ?? "";
            outlookSettings.RefreshToken = tokenData.TryGetProperty("refresh_token", out var rt) && rt.GetString() != null ? rt.GetString()! : outlookSettings.RefreshToken;
            outlookSettings.TokenExpiration = DateTimeOffset.UtcNow.AddSeconds(tokenData.GetProperty("expires_in").GetInt32());

            _logger.LogInformation("Successfully refreshed token for {Username}", outlookSettings.Username);

            return outlookSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for {Username}", outlookSettings.Username);
            throw;
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Testing Outlook/Graph API connection for {Username}", outlookSettings.Username);

            var url = $"{GraphApiBaseUrl}/me";

            var (statusCode, body) = await _api.ExecuteHttpAsync(
                provider: "microsoft",
                operation: "Graph.Me",
                limiterGroup: "profile",
                accountKey: outlookSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                    return _httpClient.SendAsync(request, ct);
                },
                parseAsync: async (response, ct) =>
                {
                    var text = await response.Content.ReadAsStringAsync(ct);
                    return ((int)response.StatusCode, text);
                },
                ensureSuccess: false,
                cancellationToken: cancellationToken);

            if (statusCode < 200 || statusCode >= 300)
            {
                return new ConnectionTestResult
                {
                    Success = false,
                    ErrorMessage = $"Authentication failed: {(HttpStatusCode)statusCode}",
                    Details = body,
                };
            }

            var userProfile = JsonSerializer.Deserialize<JsonElement>(body);
            var userPrincipalName = userProfile.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : "Unknown";

            _logger.LogInformation("Outlook connection test successful for {Username}", userPrincipalName);

            return new ConnectionTestResult
            {
                Success = true,
                Details = $"Successfully connected as {userPrincipalName}",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outlook connection test failed for {Username}", outlookSettings.Username);

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

    private MailMessage ConvertToMailMessage(JsonElement msgElement)
    {
        var messageId = msgElement.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;
        var subject = msgElement.TryGetProperty("subject", out var subj) ? subj.GetString() ?? string.Empty : string.Empty;
        var isRead = msgElement.TryGetProperty("isRead", out var read) && read.GetBoolean();
        var hasAttachments = msgElement.TryGetProperty("hasAttachments", out var attach) && attach.GetBoolean();

        var receivedDate = DateTimeOffset.MinValue;
        if (msgElement.TryGetProperty("receivedDateTime", out var receivedDt))
        {
            DateTimeOffset.TryParse(receivedDt.GetString(), out receivedDate);
        }

        var webLink = msgElement.TryGetProperty("webLink", out var link) ? link.GetString() : null;

        var fromName = string.Empty;
        var fromEmail = string.Empty;
        var fromDomain = string.Empty;
        var fromFull = string.Empty;

        if (msgElement.TryGetProperty("from", out var fromObj) && fromObj.TryGetProperty("emailAddress", out var fromEmailObj))
        {
            fromName = fromEmailObj.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty;
            fromEmail = fromEmailObj.TryGetProperty("address", out var addr) ? addr.GetString() ?? string.Empty : string.Empty;

            if (!string.IsNullOrEmpty(fromEmail) && fromEmail.Contains('@'))
            {
                fromDomain = fromEmail.Split('@')[1];
            }

            fromFull = !string.IsNullOrEmpty(fromName) ? $"{fromName} <{fromEmail}>" : fromEmail;
        }

        var toList = new List<string>();
        if (msgElement.TryGetProperty("toRecipients", out var toRecipients))
        {
            foreach (var recipient in toRecipients.EnumerateArray())
            {
                if (recipient.TryGetProperty("emailAddress", out var emailObj) && emailObj.TryGetProperty("address", out var recipientAddr))
                {
                    var address = recipientAddr.GetString();
                    if (!string.IsNullOrEmpty(address))
                    {
                        toList.Add(address);
                    }
                }
            }
        }

        var flags = new List<string>();
        if (msgElement.TryGetProperty("categories", out var categories))
        {
            foreach (var category in categories.EnumerateArray())
            {
                var cat = category.GetString();
                if (!string.IsNullOrEmpty(cat))
                {
                    flags.Add(cat);
                }
            }
        }

        return new MailMessage
        {
            MessageId = messageId,
            From = fromFull,
            FromName = fromName,
            FromEmail = fromEmail,
            FromDomain = fromDomain,
            To = toList,
            Subject = subject,
            ReceivedDate = receivedDate,
            IsRead = isRead,
            HasAttachments = hasAttachments,
            Flags = flags,
            SourceLink = webLink,
        };
    }

    private async Task<Dictionary<string, string>> GetCachedFoldersAsync(CancellationToken cancellationToken)
    {
        var cachedFolders = await _folderCache.GetAllFoldersAsync(outlookSettings.TenantId, outlookSettings.AccountId, cancellationToken);

        if (cachedFolders.Count > 0)
        {
            _logger.LogDebug("Retrieved {Count} cached folders for {Username}", cachedFolders.Count, outlookSettings.Username);
            return cachedFolders;
        }

        var folderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await FetchFoldersRecursiveAsync(null, string.Empty, folderMap, cancellationToken);

        _logger.LogDebug("Fetched and cached {Count} folders for {Username}", folderMap.Count, outlookSettings.Username);

        return folderMap;
    }

    private async Task FetchFoldersRecursiveAsync(string? parentFolderId, string parentPath, Dictionary<string, string> folderMap, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrEmpty(parentFolderId)
            ? $"{GraphApiBaseUrl}/me/mailFolders?$select=displayName,id"
            : $"{GraphApiBaseUrl}/me/mailFolders/{parentFolderId}/childFolders?$select=displayName,id";

        url += "&includeHiddenFolders=true";

        var content = await _api.ExecuteHttpAsync(
            provider: "microsoft",
            operation: "Graph.MailFolders.List",
            limiterGroup: "list",
            accountKey: outlookSettings.Username,
            maxConcurrency: 2,
            sendAsync: ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                return _httpClient.SendAsync(request, ct);
            },
            parseAsync: async (response, ct) =>
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return doc.RootElement.Clone();
            },
            ensureSuccess: true,
            cancellationToken: cancellationToken);

        if (content.TryGetProperty("value", out var foldersArray))
        {
            foreach (var folder in foldersArray.EnumerateArray())
            {
                if (folder.TryGetProperty("displayName", out var name) && folder.TryGetProperty("id", out var id))
                {
                    var nameStr = name.GetString();
                    var idStr = id.GetString();

                    if (!string.IsNullOrEmpty(nameStr) && !string.IsNullOrEmpty(idStr))
                    {
                        var fullPath = string.IsNullOrEmpty(parentPath) ? nameStr : $"{parentPath}/{nameStr}";

                        folderMap[fullPath] = idStr;

                        await _folderCache.SetFolderIdAsync(outlookSettings.TenantId, outlookSettings.AccountId, fullPath, idStr, cancellationToken);

                        await FetchFoldersRecursiveAsync(idStr, fullPath, folderMap, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Skipping folder with null/empty name or id: name='{Name}', id='{Id}'", nameStr, idStr);
                    }
                }
                else
                {
                    _logger.LogWarning("Folder element missing displayName or id property");
                }
            }
        }
        else
        {
            _logger.LogWarning("No 'value' property found in API response for {Url}", url);
        }
    }

    private async Task UpdateMessagePropertyAsync(string messageId, string propertyName, object value, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GraphApiBaseUrl}/me/messages/{messageId}";
            var updateData = new Dictionary<string, object> { { propertyName, value } };

            await _api.ExecuteHttpAsync(
                provider: "microsoft",
                operation: "Graph.Messages.PatchProperty",
                limiterGroup: "write",
                accountKey: outlookSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                    request.Content = JsonContent.Create(updateData);
                    return _httpClient.SendAsync(request, ct);
                },
                parseAsync: (_, _) => Task.FromResult(true),
                ensureSuccess: true,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully updated property {Property} for message {MessageId} for {Username}", propertyName, messageId, outlookSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update property {Property} for message {MessageId} for {Username}", propertyName, messageId, outlookSettings.Username);
            throw;
        }
    }

    private async Task<List<string>> GetMessageCategoriesAsync(string messageId, CancellationToken cancellationToken)
    {
        var url = $"{GraphApiBaseUrl}/me/messages/{messageId}?$select=categories";
        var content = await _api.ExecuteHttpAsync<JsonElement?>(
            provider: "microsoft",
            operation: "Graph.Messages.GetCategories",
            limiterGroup: "get",
            accountKey: outlookSettings.Username,
            maxConcurrency: 20,
            sendAsync: ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                return _httpClient.SendAsync(request, ct);
            },
            parseAsync: async (response, ct) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return doc.RootElement.Clone();
            },
            ensureSuccess: false,
            cancellationToken: cancellationToken);

        if (!content.HasValue)
        {
            return new List<string>();
        }

        var categories = new List<string>();

        if (content.Value.TryGetProperty("categories", out var cats))
        {
            foreach (var category in cats.EnumerateArray())
            {
                var cat = category.GetString();
                if (!string.IsNullOrEmpty(cat))
                {
                    categories.Add(cat);
                }
            }
        }

        return categories;
    }

    private async Task UpdateMessageCategoriesAsync(string messageId, List<string> categories, CancellationToken cancellationToken)
    {
        var url = $"{GraphApiBaseUrl}/me/messages/{messageId}";
        await _api.ExecuteHttpAsync(
            provider: "microsoft",
            operation: "Graph.Messages.PatchCategories",
            limiterGroup: "write",
            accountKey: outlookSettings.Username,
            maxConcurrency: 2,
            sendAsync: ct =>
            {
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outlookSettings.AccessToken);
                request.Content = JsonContent.Create(new { categories });
                return _httpClient.SendAsync(request, ct);
            },
            parseAsync: (_, _) => Task.FromResult(true),
            ensureSuccess: true,
            cancellationToken: cancellationToken);
    }
}
