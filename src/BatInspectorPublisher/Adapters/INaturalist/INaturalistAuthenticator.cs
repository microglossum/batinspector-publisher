using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Owns iNaturalist login: interactive OAuth (Authorization Code + PKCE), local token storage,
/// refresh and the OAuth-to-JWT exchange. Hosts can call <see cref="LoginAsync"/> from a "connect"
/// button; <see cref="INaturalistPublisher"/> calls <see cref="EnsureAuthenticatedAsync"/> itself.
/// </summary>
public sealed partial class INaturalistAuthenticator
{
    /// <summary>The API JWT is documented/observed to live 24 h, independent of the OAuth token's own expires_in.</summary>
    private const int JwtLifetimeSeconds = 24 * 60 * 60;

    [LoggerMessage(EventId = 2001, EventName = "StoredTokenRejected", Level = LogLevel.Warning,
        Message = "iNaturalist rejected the stored OAuth token; a new login is needed")]
    private static partial void LogStoredTokenRejected(ILogger logger, Exception error);

    [LoggerMessage(EventId = 2002, EventName = "TokenRefreshFailed", Level = LogLevel.Warning,
        Message = "Refreshing the iNaturalist token failed; falling back to interactive login")]
    private static partial void LogRefreshFailed(ILogger logger, Exception error);

    [LoggerMessage(EventId = 2003, EventName = "LoggedIn", Level = LogLevel.Information, Message = "Logged in to iNaturalist as {Username}")]
    private static partial void LogLoggedIn(ILogger logger, string username);

    private readonly INaturalistApiClient _api;
    private readonly OAuthFlow _flow;
    private readonly INaturalistTokenStore _store;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates an authenticator.</summary>
    /// <param name="options">Adapter configuration; <see cref="INaturalistOptions.ClientId"/> is required.</param>
    /// <param name="httpClient">HTTP client to use. Its timeout also applies to evidence uploads.</param>
    /// <param name="tokenStore">Where to keep the login. Defaults to a <see cref="ProtectedFileTokenStore"/> (Windows only).</param>
    /// <param name="prompt">How to show the login URL to the user. Defaults to opening the system browser.</param>
    /// <param name="logger">Optional logger.</param>
    public INaturalistAuthenticator(
        INaturalistOptions options,
        HttpClient httpClient,
        INaturalistTokenStore? tokenStore = null,
        AuthorizationPrompt? prompt = null,
        ILogger<INaturalistAuthenticator>? logger = null)
    {
        options.ValidateForOAuth();
        _logger = logger ?? NullLogger<INaturalistAuthenticator>.Instance;
        _api = new INaturalistApiClient(options, httpClient, _logger);
        _flow = new OAuthFlow(options, httpClient, prompt, _logger);
        _store = tokenStore ?? new ProtectedFileTokenStore(logger: _logger);
    }

    /// <summary>True if a login is stored (it may still need a refresh).</summary>
    public bool HasStoredToken => _store.Load() is not null;

    /// <summary>The iNaturalist login of the stored user, or null.</summary>
    public string? StoredUsername => _store.Load()?.Username;

    /// <summary>Runs the interactive browser login and stores the result.</summary>
    public async Task<INaturalistToken> LoginAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await LoginCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns a valid API token: the stored one, a silently renewed one (from the stored OAuth token,
    /// or a refresh token if one exists), or - only if neither is possible - the result of a new interactive login.
    /// </summary>
    public async Task<string> EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stored = _store.Load();
            if (stored is not null && !stored.IsLikelyExpired(DateTimeOffset.UtcNow))
            {
                return stored.AccessToken;
            }

            // iNaturalist OAuth access tokens never expire and no refresh tokens are issued (its
            // Doorkeeper config: access_token_expires_in nil), so an expired API JWT is replaced
            // silently by exchanging the stored OAuth token again. The browser login happens once.
            // Only a rejection of the token (revoked) falls through to another login; a network
            // error propagates instead of unexpectedly opening a browser.
            if (stored?.OAuthAccessToken is { Length: > 0 } oauthToken)
            {
                try
                {
                    var jwt = await _api.ExchangeOAuthTokenForJwtAsync(oauthToken, ct);
                    return SaveRenewed(stored, jwt, oauthToken, stored.RefreshToken).AccessToken;
                }
                catch (INaturalistApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    LogStoredTokenRejected(_logger, ex);
                }
            }

            if (stored?.RefreshToken is { Length: > 0 } refreshToken)
            {
                try
                {
                    var refreshed = await _flow.RefreshAsync(refreshToken, ct);
                    var jwt = await _api.ExchangeOAuthTokenForJwtAsync(refreshed.AccessToken, ct);
                    // Some servers omit refresh_token when it did not rotate: keep the old one.
                    return SaveRenewed(stored, jwt, refreshed.AccessToken, refreshed.RefreshToken ?? stored.RefreshToken).AccessToken;
                }
                catch (Exception ex) when (ex is INaturalistApiException or InvalidOperationException)
                {
                    LogRefreshFailed(_logger, ex);
                }
            }

            return (await LoginCoreAsync(ct)).AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the stored login.</summary>
    public void Logout() => _store.Clear();

    private INaturalistToken SaveRenewed(INaturalistToken stored, string jwt, string oauthAccessToken, string? refreshToken)
    {
        var token = stored with
        {
            AccessToken = jwt,
            OAuthAccessToken = oauthAccessToken,
            RefreshToken = refreshToken,
            ObtainedAtUtc = DateTimeOffset.UtcNow,
            ExpiresInSeconds = JwtLifetimeSeconds,
        };
        _store.Save(token);
        return token;
    }

    private async Task<INaturalistToken> LoginCoreAsync(CancellationToken ct)
    {
        // Fail before the browser opens: a login that cannot be stored would be authorized for nothing.
        if (!_store.CanSave)
        {
            throw new PlatformNotSupportedException(
                "The token store cannot save a login, so signing in would be pointless. For ProtectedFileTokenStore on a non-Windows platform pass allowPlaintextOnNonWindows: true, or supply your own INaturalistTokenStore.");
        }

        var oauth = await _flow.AuthorizeAsync(ct);
        var jwt = await _api.ExchangeOAuthTokenForJwtAsync(oauth.AccessToken, ct);
        var username = await _api.GetAuthenticatedUsernameAsync(jwt, ct);
        var token = new INaturalistToken
        {
            AccessToken = jwt,
            OAuthAccessToken = oauth.AccessToken,
            RefreshToken = oauth.RefreshToken,
            Username = username,
            ObtainedAtUtc = DateTimeOffset.UtcNow,
            ExpiresInSeconds = JwtLifetimeSeconds,
        };
        _store.Save(token);
        LogLoggedIn(_logger, username);
        return token;
    }
}
