using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using Atlas.Extensions;
using Atlas.Mvc;
using Atlas.Settings;
using Atlas.Email.Abstractions;
using Atlas.Email.Models;
using Atlas.Email.Settings;
using Atlas.Email.Providers;
using Atlas.Email.Security;

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
    private readonly IOAuthStateStore? _stateStore;
    private readonly OAuthFlowSettings _flowSettings;

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
        IOAuthStateStore? stateStore = null,
        OAuthFlowSettings? flowSettings = null,
        GoogleAppSettings? googleSettings = null)
    {
        _logger = logger;
        _googleSettings = googleSettings;
        _accountStore = accountStore;
        _folderCacheService = folderCacheService;
        _httpClientFactory = httpClientFactory;
        _userContext = userContext;
        _stateStore = stateStore;
        _flowSettings = flowSettings ?? new OAuthFlowSettings();
    }

    /// <summary>
    /// Initiates the OAuth flow by returning the authorization URL.
    /// Frontend should redirect the user to this URL.
    /// </summary>
    [HttpGet("authorize-url")]
    [Authorize]
    public async Task<IActionResult> GetAuthorizeUrl([FromQuery] string returnUrl, [FromQuery] string? accountId = null)
    {
        if (_stateStore == null || _googleSettings == null)
        {
            return base.Problem("Server is not configured for Google OAuth.");
        }

        // Validated HERE, on an authenticated request that can reject cleanly -- not on the
        // anonymous callback, whose only recourse is an error page. The stored value is therefore
        // trusted at redirect time by construction.
        if (!OAuthReturnUrlValidator.IsAllowed(returnUrl, _flowSettings.AllowedReturnOrigins))
        {
            _logger.LogWarning(
                "Rejected Google OAuth authorize-url: returnUrl {ReturnUrl} is not an allowed origin", returnUrl);
            return BadRequest("returnUrl is not an allowed origin.");
        }

        if (!string.IsNullOrEmpty(accountId))
        {
            // sift#5: accountId is caller-supplied and this flow later overwrites that account's
            // stored credentials. Verify ownership HERE, while the request is still authenticated
            // -- the callback that performs the write is [AllowAnonymous], where
            // UserContext.TenantId is null and both the tenant query filter and the permission
            // check degrade to allow-all. NotFound rather than Forbid, deliberately: account ids
            // are ULIDs and ULIDs are guessable within a millisecond, so "exists but is not yours"
            // must not be distinguishable from "does not exist".
            var existing = await _accountStore.GetByIdAsync(accountId);
            if (!MailAccountOwnership.IsOwnedBy(existing, _userContext.TenantId, _userContext.UserId))
            {
                _logger.LogWarning(
                    "Rejected Google OAuth authorize-url for accountId {AccountId}: not owned by tenant {TenantId} user {UserId}",
                    accountId, _userContext.TenantId, _userContext.UserId);
                return NotFound();
            }
        }

        var redirectUri = $"{Request.Scheme}://{Request.Host}/api/GoogleOAuth/callback";

        var stateToken = OAuthStateToken.New();

        await _stateStore.CreateAsync(new OAuthFlowState
        {
            StateToken = stateToken,
            Provider = "google",
            ReturnUrl = returnUrl,
            TenantId = this._userContext.TenantId,
            UserId = this._userContext.UserId,
            AuthUserId = this._userContext.AuthUserId,
            RowId = accountId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_flowSettings.StateTtlMinutes),
        });

        var authorizeUrl = $"{AuthorizeUrl}?" +
                           $"client_id={Uri.EscapeDataString(_googleSettings.ClientId)}&" +
                           $"response_type=code&" +
                           $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                           $"scope={Uri.EscapeDataString(RequiredScopes)}&" +
                           $"access_type=offline&" +
                           $"prompt=consent&" +
                           $"state={Uri.EscapeDataString(stateToken)}";

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
        if (_stateStore == null)
        {
            return base.Problem("Server is not configured for Google OAuth.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogError("Google OAuth provider returned an error: {Error}", error);
            return RedirectToError("provider_error");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return RedirectToError("state_invalid");
        }

        // Consumed BEFORE the exchange: single-use has to be enforced before any work is done,
        // or a replayed state could drive repeated token exchanges. The trade is that a transient
        // exchange failure burns the state and the user restarts -- at-most-once, deliberately.
        //
        // Unknown, expired, already-consumed and wrong-provider are indistinguishable on purpose,
        // so no oracle is handed out. This also replaces the NullReferenceException the old code
        // threw here, which surfaced to users as "?error=Object reference not set...".
        var flow = await _stateStore.TryConsumeAsync(state, "google");
        if (flow == null)
        {
            _logger.LogWarning("Google OAuth callback rejected: state invalid, expired or already used");
            return RedirectToError("state_invalid");
        }

        try
        {
            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/GoogleOAuth/callback";
            var tokenResponse = await ExchangeCodeForTokens(code, redirectUri);

            if (tokenResponse == null)
            {
                return Redirect($"{flow.ReturnUrl}?error=token_exchange_failed");
            }

            var userEmail = await GetUserEmail(tokenResponse.AccessToken);

            await SaveAccountWithTokens(flow, userEmail, tokenResponse);

            _logger.LogInformation("Successfully authenticated Google account for user {UserEmail}", userEmail);

            return Redirect($"{flow.ReturnUrl}?success=true");
        }
        catch (Exception ex)
        {
            // Opaque code, never ex.Message: the old version put exception text into a redirect URL.
            _logger.LogError(ex, "Error handling Google OAuth callback");
            return Redirect($"{flow.ReturnUrl}?error=provider_error");
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

            // sift#5: same caller-supplied id, same credential overwrite below. This endpoint is
            // [Authorize], so today it is protected only incidentally by the tenant query filter --
            // which fails open whenever UserContext.UserId is null (Brokenhip unable to resolve the
            // user). One response for both "missing" and "not yours", so neither is an oracle.
            if (!MailAccountOwnership.IsOwnedBy(account, _userContext.TenantId, _userContext.UserId))
            {
                _logger.LogWarning(
                    "Rejected Google OAuth refresh-token for accountId {AccountId}: not owned by tenant {TenantId} user {UserId}",
                    request.AccountId, _userContext.TenantId, _userContext.UserId);
                return NotFound();
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

    /// <summary>
    /// Redirect target for failures that happen BEFORE a validated ReturnUrl is available.
    ///
    /// It MUST return the user to the host they are actually on. sift serves two hosts, and
    /// OAuthCallbackPage.tsx relays the result with postMessage(..., window.location.origin)
    /// while useOAuthPopup.ts drops any message whose origin differs from the opener's. Redirect
    /// a springthroughlabs user to the brutalsystems host and the error message is silently
    /// discarded -- the popup just closes and the user sees "popup_closed" instead of the real
    /// reason.
    ///
    /// Request.Host is caller-influenceable, so it is only used after it matches the allowlist;
    /// validating it is what makes it safe to use here.
    /// </summary>
    private IActionResult RedirectToError(string code)
    {
        var requestOrigin = $"{Request.Scheme}://{Request.Host}";

        var origin = OAuthReturnUrlValidator.IsAllowed(requestOrigin, _flowSettings.AllowedReturnOrigins)
            ? requestOrigin
            : _flowSettings.AllowedReturnOrigins.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(origin))
        {
            return base.Problem("OAuth is not configured.");
        }

        return Redirect($"{origin.TrimEnd('/')}/sift/oauth-callback?error={Uri.EscapeDataString(code)}");
    }


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

    private async Task SaveAccountWithTokens(OAuthFlowState flow, string userEmail, TokenResponse tokenResponse)
    {
        var accountId = flow.RowId;

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
            ? await _accountStore.GetByEmailAsync(userEmail, flow.TenantId ?? "", MailProviderType.GmailApi)
            : await _accountStore.GetByIdAsync(accountId!);

        if (account != null)
        {
            account.EncryptedSettings = encryptedSettings;
            account.ProviderType = MailProviderType.GmailApi;
            account.UserId = flow.UserId;
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
                TenantId = flow.TenantId,
                UserId =  flow.UserId
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
