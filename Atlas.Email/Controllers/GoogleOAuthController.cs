using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using Atlas.Extensions;
using Atlas.Mvc;
using Atlas.Settings;
using ByteAether.Ulid;
using Foundatio.Caching;
using Atlas.Email.Abstractions;
using Atlas.Email.Models;
using Atlas.Email.Settings;
using Atlas.Email.Providers;

namespace Atlas.Email.Controllers;

/// <summary>
/// Handles Google OAuth authentication flow for Gmail API access.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GoogleOAuthController : ControllerBase
{
    private readonly ILogger<GoogleOAuthController> _logger;
    private readonly GoogleAppSettings? _googleSettings;
    private readonly IMailAccountStore _accountStore;
    private readonly IFolderCacheService _folderCacheService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UserContext _userContext;
    private readonly ICacheClient? _cacheClient;

    private const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string UserInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo";

    private const string RequiredScopes = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/userinfo.email";

    public GoogleOAuthController(
        ILogger<GoogleOAuthController> logger,
        IMailAccountStore accountStore,
        IFolderCacheService folderCacheService,
        IHttpClientFactory httpClientFactory,
        UserContext userContext,
        ICacheClient? cacheClient = null,
        GoogleAppSettings? googleSettings = null)
    {
        _logger = logger;
        _googleSettings = googleSettings;
        _accountStore = accountStore;
        _folderCacheService = folderCacheService;
        _httpClientFactory = httpClientFactory;
        _userContext = userContext;
        _cacheClient = cacheClient;
    }

    /// <summary>
    /// Initiates the OAuth flow by returning the authorization URL.
    /// Frontend should redirect the user to this URL.
    /// </summary>
    [HttpGet("authorize-url")]
    [Authorize]
    public async Task<IActionResult> GetAuthorizeUrl([FromQuery] string returnUrl, [FromQuery] string? accountId = null)
    {
        if (_cacheClient == null || _googleSettings == null)
        {
            return base.Problem("Server is not configured for Google OAuth.");
        }

        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/GoogleOAuth/callback";

        var limit = 20;
        var cacheKey = $"google-oauth-authorize-url-{Ulid.New().ToString()}";
        while ((await _cacheClient.GetAsync<string>(cacheKey)).Value != null)
        {
            cacheKey = $"google-oauth-authorize-url-{Ulid.New().ToString()}";
            if (--limit == 0)
            {
                return base.Problem("Could not generate unique cache key for Google OAuth authorize URL.");
            }
        }

        var acl = new AnonymousCallbackLink()
        {
            RowId = accountId,
            TenantId = this._userContext.TenantId,
            AuthUserId = this._userContext.AuthUserId,
            ReturnUrl = returnUrl,
        };
        await _cacheClient.SetAsync(cacheKey, acl, TimeSpan.FromMinutes(10));

        var state = cacheKey;
        var stateBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(state));

        var authorizeUrl = $"{AuthorizeUrl}?" +
                           $"client_id={Uri.EscapeDataString(_googleSettings.ClientId)}&" +
                           $"response_type=code&" +
                           $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                           $"scope={Uri.EscapeDataString(RequiredScopes)}&" +
                           $"access_type=offline&" +
                           $"prompt=consent&" +
                           $"state={Uri.EscapeDataString(stateBase64)}";

        _logger.LogInformation("Generated Google OAuth authorize URL for user {UserId}", _userContext.AuthUserId);

        return Ok(new { authorizeUrl });
    }

    /// <summary>
    /// Handles the OAuth callback from Google.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (_cacheClient == null)
        {
            return base.Problem("Server is not configured for Google OAuth.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogError("OAuth error: {Error}", error);
            return Redirect($"/accounts?error={Uri.EscapeDataString(error)}");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return BadRequest("Missing code or state parameter");
        }

        var returnUrl = string.Empty;
        try
        {
            var cacheKey = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(state));
            var acl = (await _cacheClient.GetAsync<AnonymousCallbackLink>(cacheKey)).Value;
            returnUrl = acl.ReturnUrl;

            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/GoogleOAuth/callback";
            var tokenResponse = await ExchangeCodeForTokens(code, redirectUri);

            if (tokenResponse == null)
            {
                return Redirect($"{returnUrl}?error=token_exchange_failed");
            }

            var userEmail = await GetUserEmail(tokenResponse.AccessToken);
            acl.UserEmail = userEmail;

            await SaveAccountWithTokens(acl, tokenResponse);

            _logger.LogInformation("Successfully authenticated Google account for user {UserEmail}", userEmail);

            return Redirect($"{returnUrl}?success=true");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OAuth callback");
            return Redirect($"{returnUrl}/accounts?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>
    /// Refreshes an expired access token using the stored refresh token.
    /// </summary>
    [HttpPost("refresh-token")]
    [Authorize]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrEmpty(request.AccountId))
        {
            return BadRequest("AccountId is required");
        }

        try
        {
            var account = await _accountStore.GetByIdAsync(request.AccountId);
            if (account == null)
            {
                return NotFound("Account not found");
            }

            var settings = MailSettings.FromEncryptedJson(account.EncryptedSettings!);
            if (settings is not GmailApiSettings gmailSettings)
            {
                return BadRequest("Account is not a Gmail account");
            }

            settings.AccountId = account.Id!;
            settings.TenantId = account.TenantId!;
            var mailProvider = new GoogleMailProvider(gmailSettings, _folderCacheService);
            var updatedSettings = await mailProvider.RefreshTokenAsync();

            account.EncryptedSettings = updatedSettings.ToEncryptedJson();
            await _accountStore.SaveAsync(account);

            _logger.LogInformation("Successfully refreshed token for account {AccountId}", account.Id);

            return Ok(new { success = true, expiresAt = ((GmailApiSettings)updatedSettings).TokenExpiry });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token for account {AccountId}", request.AccountId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Private helper methods

    private async Task<TokenResponse?> ExchangeCodeForTokens(string code, string redirectUri)
    {
        var client = _httpClientFactory.CreateClient();

        var parameters = new Dictionary<string, string>
        {
            { "client_id", _googleSettings!.ClientId },
            { "client_secret", _googleSettings!.ClientSecret },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "grant_type", "authorization_code" }
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await client.PostAsync(TokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Token exchange failed: {Error}", error);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);

        return new TokenResponse
        {
            AccessToken = tokenData.GetProperty("access_token").GetString() ?? "",
            RefreshToken = tokenData.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn = tokenData.GetProperty("expires_in").GetInt32()
        };
    }

    private async Task<string> GetUserEmail(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var userData = JsonSerializer.Deserialize<JsonElement>(json);

        return userData.GetProperty("email").GetString()
               ?? throw new Exception("Could not retrieve user email");
    }

    private async Task SaveAccountWithTokens(AnonymousCallbackLink acl, TokenResponse tokenResponse)
    {
        if (acl.UserEmail == null)
        {
            throw new ArgumentNullException(nameof(acl.UserEmail));
        }

        var userEmail = acl.UserEmail;
        var accountId = acl.RowId;

        var gmailSettings = new GmailApiSettings
        {
            Username = userEmail,
            ClientId = _googleSettings!.ClientId,
            ClientSecret = _googleSettings!.ClientSecret,
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? string.Empty,
            TokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };

        var encryptedSettings = gmailSettings.ToEncryptedJson();

        var account = accountId.IsNullOrWhiteSpace()
            ? await _accountStore.GetByEmailAsync(userEmail, MailProviderType.GmailApi)
            : await _accountStore.GetByIdAsync(accountId!);

        if (account != null)
        {
            account.EncryptedSettings = encryptedSettings;
            account.ProviderType = MailProviderType.GmailApi;
            account.Email = userEmail;
            await _accountStore.SaveAsync(account);
        }
        else
        {
            var newAccount = new MailAccountRecord
            {
                Name = $"{userEmail} (Gmail)",
                Email = userEmail,
                ProviderType = MailProviderType.GmailApi,
                EncryptedSettings = encryptedSettings,
                IsActive = true,
                TenantId = acl.TenantId,
            };

            await _accountStore.SaveAsync(newAccount);
        }
    }

    private sealed class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }
}
