using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Web;
using Atlas.Helpers;
using Microsoft.Extensions.Logging;
using Atlas.Email.Models;
using Atlas.Email.Settings;

namespace Atlas.Email.Providers;

/// <summary>
/// Google Gmail API implementation of the IMailProvider interface.
/// </summary>
public class GoogleMailProvider : IMailProvider
{
    private readonly ILogger<GoogleMailProvider> _logger = Logging.CreateLogger<GoogleMailProvider>();
    private readonly ApiCallExecutor _api = new ApiCallExecutor(Logging.CreateLogger<ApiCallExecutor>());
    private readonly HttpClient _httpClient = new HttpClient();
    private const string GmailApiBaseUrl = "https://gmail.googleapis.com/gmail/v1";
    private readonly IFolderCacheService _folderCache;
    private readonly GmailApiSettings gmailSettings;

    public GoogleMailProvider(MailSettings mailSettings, IFolderCacheService folderCache)
    {
        if (mailSettings is not GmailApiSettings settings)
        {
            throw new ArgumentException("Invalid settings type. Expected GmailApiSettings.", nameof(mailSettings));
        }

        gmailSettings = settings;
        _folderCache = folderCache;
    }

    public async Task<IEnumerable<MailMessage>> FetchMessagesAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Fetching messages since {Since} until {Until} for {Username} (max: {MaxCount})", request.Since, request.Until, gmailSettings.Username, request.MaxCount);

            var messages = new List<MailMessage>();
            var sinceUnix = request.Since.ToUnixTimeSeconds();
            var query = $"after:{sinceUnix}";

            if (!string.IsNullOrEmpty(request.Folder))
            {
                var systemFolders = new[]
                {
                    "inbox", "sent", "spam", "trash", "anywhere", "drafts", "important", "starred",
                };
                if (systemFolders.Contains(request.Folder.ToLower()))
                {
                    query = $"in:{request.Folder} {query}";
                }
                else
                {
                    var labelId = await GetFolderIdAsync(request.Folder, cancellationToken);
                    query = $"label:{request.Folder} {query}";
                }
            }

            if (request.Until.HasValue)
            {
                var untilUnix = request.Until.Value.ToUnixTimeSeconds();
                query += $" before:{untilUnix}";
            }

            string? pageToken = null;
            const int pageSize = 20;
            var totalFetched = 0;

            int processedMessages = 0;
            int totalMessages = await this.FetchMessagesCountAsync(request);

            _logger.LogDebug("------------ Total messages to process: {TotalMessages}", totalMessages);

            do
            {
                var listUrl = $"{GmailApiBaseUrl}/users/me/messages?q={HttpUtility.UrlEncode(query)}&maxResults={pageSize}";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    listUrl += $"&pageToken={pageToken}";
                }

                var listContent = await _api.ExecuteHttpAsync(
                    provider: "google",
                    operation: "Gmail.Messages.List",
                    limiterGroup: "list",
                    accountKey: gmailSettings.Username,
                    maxConcurrency: 2,
                    sendAsync: ct =>
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, listUrl);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                        return _httpClient.SendAsync(req, ct);
                    },
                    parseAsync: async (response, ct) =>
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        return doc.RootElement.Clone();
                    },
                    ensureSuccess: true,
                    cancellationToken: cancellationToken);

                if (!listContent.TryGetProperty("messages", out var messagesArray) || messagesArray.GetArrayLength() == 0)
                {
                    _logger.LogDebug("No more messages available");
                    break;
                }

                var tasks = new List<Task<MailMessage?>>();
                foreach (var msgElement in messagesArray.EnumerateArray())
                {
                    if (msgElement.TryGetProperty("id", out var messageId))
                    {
                        tasks.Add(GetMessageDetailsAsync(messageId.GetString()!, request.Folder, cancellationToken));
                    }
                }

                var messageResults = await Task.WhenAll(tasks);
                var validMessages = messageResults.Where(m => m != null).ToList();
                totalFetched += validMessages.Count;

                var filteredMessages = new List<MailMessage>();
                if (request.Filter != null)
                {
                    foreach (var message in validMessages)
                    {
                        if (message != null && await request.Filter(message, processedMessages++, totalMessages))
                        {
                            filteredMessages.Add(message);
                        }
                    }
                }
                else
                {
                    filteredMessages = validMessages!;
                }

                messages.AddRange(filteredMessages);

                _logger.LogDebug("Page fetched: {PageSize} messages, {FilteredCount} passed filter, {TotalCount} total collected (target: {MaxCount})", validMessages.Count, filteredMessages.Count, messages.Count, request.MaxCount);

                if (messages.Count >= request.MaxCount)
                {
                    _logger.LogDebug("Reached max count of {MaxCount}, stopping pagination", request.MaxCount);
                    break;
                }

                pageToken = listContent.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
            } while (!string.IsNullOrEmpty(pageToken));

            var finalMessages = messages.OrderBy(m => m.ReceivedDate).Take(request.MaxCount).ToList();

            _logger.LogInformation("Successfully fetched {Count} messages (from {TotalFetched} total) for {Username}", finalMessages.Count, totalFetched, gmailSettings.Username);

            return finalMessages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages for {Username}", gmailSettings.Username);
            throw;
        }
    }

    public async Task<int> FetchMessagesCountAsync(FetchMessagesRequest request, CancellationToken cancellationToken = default)
    {
        var since = request.Since;
        var until = request.Until;
        var folder = request.Folder;

        try
        {
            var sinceUnix = since.ToUnixTimeSeconds();
            var query = $"after:{sinceUnix}";

            if (!string.IsNullOrEmpty(folder))
            {
                var systemFolders = new[]
                {
                    "inbox", "sent", "spam", "trash", "anywhere", "drafts", "important", "starred",
                };
                if (systemFolders.Contains(folder.ToLower()))
                {
                    query = $"in:{folder} {query}";
                }
                else
                {
                    var labelId = await GetFolderIdAsync(folder, cancellationToken);
                    query = $"label:{folder} {query}";
                }
            }

            if (until.HasValue)
            {
                var untilUnix = until.Value.ToUnixTimeSeconds();
                query += $" before:{untilUnix}";
            }

            int pageSize = 500;
            string? pageToken = null;
            int totalCount = 0;
            do
            {
                var listUrl = $"{GmailApiBaseUrl}/users/me/messages?q={HttpUtility.UrlEncode(query)}&maxResults={pageSize}";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    listUrl += $"&pageToken={pageToken}";
                }

                var listContent = await _api.ExecuteHttpAsync(
                    provider: "google",
                    operation: "Gmail.Messages.ListCount",
                    limiterGroup: "list",
                    accountKey: gmailSettings.Username,
                    maxConcurrency: 2,
                    sendAsync: ct =>
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, listUrl);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                        return _httpClient.SendAsync(req, ct);
                    },
                    parseAsync: async (response, ct) =>
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        return doc.RootElement.Clone();
                    },
                    ensureSuccess: true,
                    cancellationToken: cancellationToken);

                if (!listContent.TryGetProperty("messages", out var messagesArray) || messagesArray.GetArrayLength() == 0)
                {
                    _logger.LogDebug("No more messages available");
                    return 0;
                }

                var cnt = messagesArray.GetArrayLength();
                totalCount += cnt;
                if (cnt < pageSize)
                {
                    break;
                }

                pageToken = listContent.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
            } while (!string.IsNullOrEmpty(pageToken));

            return totalCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch messages for {Username}", gmailSettings.Username);
            throw;
        }
    }

    public async Task MoveMessageAsync(MailMessage emessage, string folderName, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var (mustExist, removeFromOthers, parsedFolderName) = ParseFolderFlags(folderName);

            var labelId = await GetFolderIdAsync(parsedFolderName, cancellationToken);
            if (labelId != null && mustExist)
            {
                throw new InvalidOperationException($"Folder/label {parsedFolderName} does not exist");
            }

            if (labelId == null)
            {
                labelId = await this.CreateFolderAsync(parsedFolderName, cancellationToken);
            }

            var labelsToRemove = new List<string>();
            if (removeFromOthers)
            {
                var url = $"{GmailApiBaseUrl}/users/me/messages/{messageId}";

                var content = await _api.ExecuteHttpAsync<JsonElement>(
                    provider: "google",
                    operation: "Gmail.Messages.GetLabelsForMove",
                    limiterGroup: "get",
                    accountKey: gmailSettings.Username,
                    maxConcurrency: 20,
                    sendAsync: ct =>
                    {
                        var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                        return _httpClient.SendAsync(req, ct);
                    },
                    parseAsync: async (response, ct) =>
                    {
                        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        return doc.RootElement.Clone();
                    },
                    ensureSuccess: true,
                    cancellationToken: cancellationToken);

                if (content.TryGetProperty("labelIds", out var labelIds))
                {
                    foreach (var label in labelIds.EnumerateArray())
                    {
                        var labelStr = label.GetString();
                        if (!string.IsNullOrEmpty(labelStr) && labelStr != labelId)
                        {
                            labelsToRemove.Add(labelStr);
                        }
                    }
                }

                if (labelsToRemove.Count > 0)
                {
                    await ModifyMessageLabelsAsync(messageId, new[] { labelId }, labelsToRemove.ToArray(), cancellationToken);
                }
            }
            else
            {
                labelsToRemove.Add(emessage.Folders.FirstOrDefault()?.Name ?? "INBOX");
            }

            await ModifyMessageLabelsAsync(messageId, new[] { labelId }, labelsToRemove.ToArray(), cancellationToken);

            _logger.LogInformation("Successfully moved message {MessageId} to folder {Folder} for {Username}", messageId, folderName, gmailSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move message {MessageId} to folder {Folder} for {Username}", messageId, folderName, gmailSettings.Username);
            throw;
        }
    }

    public async Task DeleteMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var url = $"{GmailApiBaseUrl}/users/me/messages/{messageId}";

            await _api.ExecuteHttpAsync(
                provider: "google",
                operation: "Gmail.Messages.Delete",
                limiterGroup: "write",
                accountKey: gmailSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Delete, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                    return _httpClient.SendAsync(req, ct);
                },
                parseAsync: (_, _) => Task.FromResult(true),
                ensureSuccess: true,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Successfully deleted message {MessageId} for {Username}", messageId, gmailSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message {MessageId} for {Username}", messageId, gmailSettings.Username);
            throw;
        }
    }

    public async Task MarkAsReadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        await ModifyMessageLabelsAsync(messageId, null, new[] { "UNREAD" }, cancellationToken);
    }

    public async Task MarkAsUnreadAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        await ModifyMessageLabelsAsync(messageId, new[] { "UNREAD" }, null, cancellationToken);
    }

    public async Task ArchiveMessageAsync(MailMessage emessage, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        await ModifyMessageLabelsAsync(messageId, null, new[] { "INBOX" }, cancellationToken);
    }

    public async Task AddFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var labelId = await GetOrCreateLabelAsync(flag, cancellationToken);
            await ModifyMessageLabelsAsync(messageId, new[] { labelId }, null, cancellationToken);

            _logger.LogDebug("Successfully added flag {Flag} to message {MessageId} for {Username}", flag, messageId, gmailSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add flag {Flag} to message {MessageId} for {Username}", flag, messageId, gmailSettings.Username);
            throw;
        }
    }

    public async Task RemoveFlagAsync(MailMessage emessage, string flag, CancellationToken cancellationToken = default)
    {
        var messageId = emessage.MessageId;
        try
        {
            var labelId = await GetFolderIdAsync(flag, cancellationToken);
            if (labelId != null)
            {
                await ModifyMessageLabelsAsync(messageId, null, new[] { labelId }, cancellationToken);
                _logger.LogDebug("Successfully removed flag {Flag} from message {MessageId} for {Username}", flag, messageId, gmailSettings.Username);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove flag {Flag} from message {MessageId} for {Username}", flag, messageId, gmailSettings.Username);
            throw;
        }
    }

    public async Task<string> CreateFolderAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating label {FolderName} for {Username}", folderName, gmailSettings.Username);

            if (folderName.Contains('/'))
            {
                var parts = folderName.Split('/');
                var currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";

                    var existingLabelId = await GetFolderIdAsync(currentPath, cancellationToken);
                    if (existingLabelId == null)
                    {
                        await CreateSingleLabelAsync(currentPath, cancellationToken);
                        _logger.LogDebug("Created parent label {LabelPath} for {Username}", currentPath, gmailSettings.Username);
                    }
                }

                var finalLabelId = await GetFolderIdAsync(folderName, cancellationToken);
                if (finalLabelId == null)
                {
                    throw new InvalidOperationException($"Failed to retrieve ID for created label {folderName}");
                }

                return finalLabelId;
            }
            else
            {
                return await CreateSingleLabelAsync(folderName, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create label {FolderName} for {Username}", folderName, gmailSettings.Username);
            throw;
        }
    }

    private async Task<string> CreateSingleLabelAsync(string labelName, CancellationToken cancellationToken)
    {
        var url = $"{GmailApiBaseUrl}/users/me/labels";
        var labelRequest = new
        {
            name = labelName,
            labelListVisibility = "labelShow",
            messageListVisibility = "show",
        };

        var labelId = await _api.ExecuteHttpAsync(
            provider: "google",
            operation: "Gmail.Labels.Create",
            limiterGroup: "write",
            accountKey: gmailSettings.Username,
            maxConcurrency: 2,
            sendAsync: ct =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                req.Content = JsonContent.Create(labelRequest);
                return _httpClient.SendAsync(req, ct);
            },
            parseAsync: async (response, ct) =>
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                return string.IsNullOrWhiteSpace(id) ? throw new InvalidOperationException("Label ID not returned from API") : id;
            },
            ensureSuccess: true,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(labelId))
        {
            throw new InvalidOperationException("Label ID was null");
        }

        _logger.LogInformation("Successfully created label {LabelName} with ID {LabelId} for {Username}", labelName, labelId, gmailSettings.Username);

        return labelId;
    }

    public async Task<IEnumerable<string>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var labelMap = await GetCachedLabelsAsync(cancellationToken);
            return labelMap.Keys.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get labels for {Username}", gmailSettings.Username);
            throw;
        }
    }

    public async Task<string?> GetFolderIdAsync(string folderName, CancellationToken cancellationToken = default)
    {
        try
        {
            var labelMap = await GetCachedLabelsAsync(cancellationToken);

            if (labelMap.TryGetValue(folderName, out var labelId))
            {
                return labelId;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get folder {FolderName} for {Username}", folderName, gmailSettings.Username);
            throw;
        }
    }

    public async Task<MailSettings> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(gmailSettings.RefreshToken))
        {
            throw new InvalidOperationException("No refresh token available");
        }

        try
        {
            _logger.LogDebug("Refreshing access token for {Username}", gmailSettings.Username);

            var parameters = new Dictionary<string, string>
            {
                { "client_id", gmailSettings.ClientId },
                { "client_secret", gmailSettings.ClientSecret },
                { "refresh_token", gmailSettings.RefreshToken },
                { "grant_type", "refresh_token" },
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Token refresh failed for {Username}: {Error}", gmailSettings.Username, error);
                throw new InvalidOperationException($"Token refresh failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<JsonElement>(json);

            gmailSettings.AccessToken = tokenData.GetProperty("access_token").GetString() ?? "";
            gmailSettings.RefreshToken = tokenData.TryGetProperty("refresh_token", out var rt) && rt.GetString() != null ? rt.GetString()! : gmailSettings.RefreshToken;
            gmailSettings.TokenExpiry = DateTime.UtcNow.AddSeconds(tokenData.GetProperty("expires_in").GetInt32());

            _logger.LogInformation("Successfully refreshed token for {Username}", gmailSettings.Username);

            return gmailSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for {Username}", gmailSettings.Username);
            throw;
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{GmailApiBaseUrl}/users/me/profile";

            var (statusCode, body) = await _api.ExecuteHttpAsync(
                provider: "google",
                operation: "Gmail.Profile",
                limiterGroup: "profile",
                accountKey: gmailSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
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
            var emailAddress = userProfile.TryGetProperty("emailAddress", out var email) ? email.GetString() : "Unknown";

            _logger.LogInformation("Gmail connection test successful for {EmailAddress}", emailAddress);

            return new ConnectionTestResult
            {
                Success = true,
                Details = $"Successfully connected as {emailAddress}",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gmail connection test failed for {Username}", gmailSettings.Username);

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

    private async Task<MailMessage?> GetMessageDetailsAsync(string messageId, string? folderName, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GmailApiBaseUrl}/users/me/messages/{messageId}";

            var content = await _api.ExecuteHttpAsync<JsonElement?>(
                provider: "google",
                operation: "Gmail.Messages.Get",
                limiterGroup: "get",
                accountKey: gmailSettings.Username,
                maxConcurrency: 20,
                sendAsync: ct =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                    return _httpClient.SendAsync(req, ct);
                },
                parseAsync: async (response, ct) =>
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        var snippet = string.IsNullOrWhiteSpace(body) ? "" : (body.Length <= 500 ? body : body.Substring(0, 500));
                        _logger.LogWarning("Failed to get message details for {MessageId}: {StatusCode}. Body: {BodySnippet}", messageId, response.StatusCode, snippet);
                        return null;
                    }

                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    return doc.RootElement.Clone();
                },
                ensureSuccess: false,
                cancellationToken: cancellationToken);

            var result = content.HasValue ? ConvertToMailMessage(content.Value) : null;
            if (result != null && folderName != null)
            {
                result.Folders.Add(new EmailFolder { Name = folderName });
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get message details for {MessageId}", messageId);
            return null;
        }
    }

    private MailMessage ConvertToMailMessage(JsonElement msgElement)
    {
        var messageId = msgElement.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty;

        var receivedDate = DateTimeOffset.MinValue;
        var subject = string.Empty;
        var fromName = string.Empty;
        var fromEmail = string.Empty;
        var fromDomain = string.Empty;
        var fromFull = string.Empty;
        var toList = new List<string>();
        var isRead = true;
        var hasAttachments = false;
        var body = string.Empty;

        if (msgElement.TryGetProperty("payload", out var payload))
        {
            if (payload.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateArray())
                {
                    if (header.TryGetProperty("name", out var headerName) && header.TryGetProperty("value", out var headerValue))
                    {
                        var name = headerName.GetString();
                        var value = headerValue.GetString();

                        switch (name?.ToLower())
                        {
                            case "subject":
                                subject = value ?? string.Empty;
                                break;
                            case "from":
                                fromFull = value ?? string.Empty;
                                ParseFromField(fromFull, out fromName, out fromEmail, out fromDomain);
                                break;
                            case "to":
                                if (!string.IsNullOrEmpty(value))
                                {
                                    toList.AddRange(ParseEmailAddresses(value));
                                }
                                break;
                            case "date":
                                if (DateTimeOffset.TryParse(value, out var parsedDate))
                                {
                                    receivedDate = parsedDate;
                                }
                                break;
                        }
                    }
                }
            }

            body = ExtractBodyFromPayload(payload);
            hasAttachments = HasAttachments(payload);
        }

        if (msgElement.TryGetProperty("labelIds", out var labelIds))
        {
            isRead = !labelIds.EnumerateArray().Any(l => l.GetString() == "UNREAD");
        }

        var flags = new List<string>();
        if (msgElement.TryGetProperty("labelIds", out var labels))
        {
            foreach (var label in labels.EnumerateArray())
            {
                var labelStr = label.GetString();
                if (!string.IsNullOrEmpty(labelStr) && !IsSystemLabel(labelStr))
                {
                    flags.Add(labelStr);
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
            Body = body,
            ReceivedDate = receivedDate,
            IsRead = isRead,
            HasAttachments = hasAttachments,
            Flags = flags,
            SourceLink = $"https://mail.google.com/mail/u/0/#inbox/{messageId}",
        };
    }

    private string ExtractBodyFromPayload(JsonElement payload)
    {
        if (payload.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data))
        {
            var encodedBody = data.GetString();
            if (!string.IsNullOrEmpty(encodedBody))
            {
                return DecodeBase64Url(encodedBody);
            }
        }

        if (payload.TryGetProperty("parts", out var parts))
        {
            return ExtractBodyFromParts(parts);
        }

        return string.Empty;
    }

    private string ExtractBodyFromParts(JsonElement parts)
    {
        var textPlain = string.Empty;
        var textHtml = string.Empty;

        foreach (var part in parts.EnumerateArray())
        {
            var mimeType = part.TryGetProperty("mimeType", out var mt) ? mt.GetString() : string.Empty;

            if (part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data))
            {
                var encodedBody = data.GetString();
                if (!string.IsNullOrEmpty(encodedBody))
                {
                    var decodedBody = DecodeBase64Url(encodedBody);

                    if (mimeType == "text/plain")
                    {
                        textPlain = decodedBody;
                    }
                    else if (mimeType == "text/html")
                    {
                        textHtml = decodedBody;
                    }
                }
            }

            if (part.TryGetProperty("parts", out var nestedParts))
            {
                var nestedBody = ExtractBodyFromParts(nestedParts);
                if (!string.IsNullOrEmpty(nestedBody))
                {
                    if (string.IsNullOrEmpty(textPlain) && !nestedBody.Contains("<html"))
                    {
                        textPlain = nestedBody;
                    }
                    else if (string.IsNullOrEmpty(textHtml))
                    {
                        textHtml = nestedBody;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(textPlain))
            return textHtml;
        if (string.IsNullOrEmpty(textHtml))
            return textPlain;

        if (IsPlainTextFallbackMessage(textPlain))
            return textHtml;

        return textPlain;
    }

    private bool IsPlainTextFallbackMessage(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return true;

        var text = plainText.Trim().ToLower();

        var fallbackPhrases = new[]
        {
            "html email", "html version", "html-only", "view this email in your browser",
            "email client does not support", "client does not support html", "enable html",
            "cannot display html", "plain text version", "not supported",
        };

        if (text.Length < 100 && fallbackPhrases.Any(phrase => text.Contains(phrase)))
        {
            return true;
        }

        if (text.Length < 200 && fallbackPhrases.Any(phrase => text.Contains(phrase)))
        {
            var sentenceCount = text.Count(c => c == '.' || c == '!' || c == '?');
            if (sentenceCount <= 2)
            {
                return true;
            }
        }

        return false;
    }

    private string DecodeBase64Url(string base64Url)
    {
        try
        {
            var base64 = base64Url.Replace('-', '+').Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            var bytes = Convert.FromBase64String(base64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode base64url string");
            return string.Empty;
        }
    }

    private bool HasAttachments(JsonElement payload)
    {
        if (payload.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("body", out var body) && body.TryGetProperty("attachmentId", out _))
                {
                    return true;
                }

                if (HasAttachments(part))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsSystemLabel(string labelId)
    {
        var systemLabels = new[]
        {
            "INBOX", "SENT", "DRAFT", "SPAM", "TRASH", "UNREAD", "STARRED", "IMPORTANT", "CHAT",
            "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS",
        };

        return systemLabels.Contains(labelId);
    }

    private void ParseFromField(string fromFull, out string fromName, out string fromEmail, out string fromDomain)
    {
        fromName = string.Empty;
        fromEmail = string.Empty;
        fromDomain = string.Empty;

        if (string.IsNullOrEmpty(fromFull))
            return;

        var match = System.Text.RegularExpressions.Regex.Match(fromFull, @"(.+?)\s*<(.+?)>");
        if (match.Success)
        {
            fromName = match.Groups[1].Value.Trim().Trim('"');
            fromEmail = match.Groups[2].Value.Trim();
        }
        else
        {
            fromEmail = fromFull.Trim();
        }

        if (!string.IsNullOrEmpty(fromEmail) && fromEmail.Contains('@'))
        {
            fromDomain = fromEmail.Split('@')[1];
        }
    }

    private IEnumerable<string> ParseEmailAddresses(string emailString)
    {
        if (string.IsNullOrEmpty(emailString))
            return new List<string>();

        return emailString.Split(',', ';').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e))
            .Select(e => System.Text.RegularExpressions.Regex.Match(e, @"<(.+?)>").Success
                ? System.Text.RegularExpressions.Regex.Match(e, @"<(.+?)>").Groups[1].Value
                : e);
    }

    private async Task<Dictionary<string, string>> GetCachedLabelsAsync(CancellationToken cancellationToken)
    {
        var cachedLabels = await _folderCache.GetAllFoldersAsync(gmailSettings.TenantId, gmailSettings.AccountId, cancellationToken);

        if (cachedLabels.Count > 0)
        {
            _logger.LogDebug("Retrieved {Count} cached labels for {Username}", cachedLabels.Count, gmailSettings.Username);
            return cachedLabels;
        }

        var url = $"{GmailApiBaseUrl}/users/me/labels";
        var content = await _api.ExecuteHttpAsync(
            provider: "google",
            operation: "Gmail.Labels.List",
            limiterGroup: "list",
            accountKey: gmailSettings.Username,
            maxConcurrency: 2,
            sendAsync: ct =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                return _httpClient.SendAsync(req, ct);
            },
            parseAsync: async (response, ct) =>
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return doc.RootElement.Clone();
            },
            ensureSuccess: true,
            cancellationToken: cancellationToken);

        var labelMap = new Dictionary<string, string>();

        if (content.TryGetProperty("labels", out var labels))
        {
            foreach (var label in labels.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var name) && label.TryGetProperty("id", out var labelId))
                {
                    var nameStr = name.GetString();
                    var idStr = labelId.GetString();

                    if (!string.IsNullOrEmpty(nameStr) && !string.IsNullOrEmpty(idStr))
                    {
                        labelMap[nameStr] = idStr;
                        await _folderCache.SetFolderIdAsync(gmailSettings.TenantId, gmailSettings.AccountId, nameStr, idStr, cancellationToken);
                    }
                }
            }
        }

        _logger.LogDebug("Fetched and cached {Count} labels for {Username}", labelMap.Count, gmailSettings.Username);

        return labelMap;
    }

    private async Task<string> GetOrCreateLabelAsync(string labelName, CancellationToken cancellationToken)
    {
        return await GetFolderIdAsync(labelName, cancellationToken) ?? await CreateFolderAsync(labelName, cancellationToken);
    }

    private async Task ModifyMessageLabelsAsync(string messageId, string[]? addLabels, string[]? removeLabels, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GmailApiBaseUrl}/users/me/messages/{messageId}/modify";
            var modifyRequest = new Dictionary<string, object>();

            if (addLabels?.Length > 0)
            {
                modifyRequest["addLabelIds"] = addLabels;
            }

            if (removeLabels?.Length > 0)
            {
                modifyRequest["removeLabelIds"] = removeLabels;
            }

            await _api.ExecuteHttpAsync(
                provider: "google",
                operation: "Gmail.Messages.Modify",
                limiterGroup: "write",
                accountKey: gmailSettings.Username,
                maxConcurrency: 2,
                sendAsync: ct =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gmailSettings.AccessToken);
                    req.Content = JsonContent.Create(modifyRequest);
                    return _httpClient.SendAsync(req, ct);
                },
                parseAsync: (_, _) => Task.FromResult(true),
                ensureSuccess: true,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully modified labels for message {MessageId} for {Username}", messageId, gmailSettings.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to modify labels for message {MessageId} for {Username}", messageId, gmailSettings.Username);
            throw;
        }
    }
}
