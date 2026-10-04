using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Shows the authorization URL to the user. The default implementation opens the system browser;
/// a host application can pass its own to present the URL in its UI.
/// </summary>
/// <param name="authorizeUrl">The iNaturalist authorization URL the user must open.</param>
/// <param name="ct">Cancellation token.</param>
public delegate Task AuthorizationPrompt(Uri authorizeUrl, CancellationToken ct);

/// <summary>
/// OAuth 2.0 Authorization Code + PKCE against iNaturalist: hands the authorize URL to the
/// <see cref="AuthorizationPrompt"/>, catches the redirect on a temporary loopback HTTP listener
/// and exchanges the returned code for tokens.
/// </summary>
internal sealed partial class OAuthFlow
{
    /// <summary>How many OS-assigned ports are tried after the configured one is found taken.</summary>
    private const int MaxFallbackPorts = 3;

    private readonly INaturalistOptions _options;
    private readonly HttpClient _http;
    private readonly AuthorizationPrompt _prompt;
    private readonly ILogger _logger;

    // The token endpoint's body holds tokens and is never logged.
    [LoggerMessage(EventId = 2101, EventName = "OAuthTokenRequest", Level = LogLevel.Debug, Message = "POST {Url} ({GrantType}) -> {Status}")]
    private static partial void LogTokenRequest(ILogger logger, string url, string grantType, int status);

    [LoggerMessage(EventId = 2102, EventName = "OAuthCannotListen", Level = LogLevel.Debug, Message = "Cannot listen on port {Port}: {Message}")]
    private static partial void LogCannotListen(ILogger logger, int port, string message);

    [LoggerMessage(EventId = 2103, EventName = "OAuthFallbackPort", Level = LogLevel.Warning,
        Message = "Port {Preferred} is in use; the login listens on port {Port} instead. iNaturalist must accept that port for the registered redirect URI.")]
    private static partial void LogFallbackPort(ILogger logger, int preferred, int port);

    public OAuthFlow(INaturalistOptions options, HttpClient http, AuthorizationPrompt? prompt = null, ILogger? logger = null)
    {
        _options = options;
        _http = http;
        _prompt = prompt ?? OpenSystemBrowserAsync;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<OAuthTokenResponse> AuthorizeAsync(CancellationToken ct)
    {
        var codeVerifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        using var listener = StartListener(_options.RedirectUri, out var redirectUri);

        var authorizeUrl = new Uri($"{_options.OAuthAuthorizeUrl}" +
            $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            "&code_challenge_method=S256");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.AuthorizationTimeout);

        string code;
        try
        {
            await _prompt(authorizeUrl, timeout.Token);
            code = await WaitForAuthorizationCodeAsync(listener, state, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"No iNaturalist login was completed within {_options.AuthorizationTimeout}.");
        }
        finally
        {
            listener.Stop();
        }

        return await ExchangeCodeForTokenAsync(code, codeVerifier, redirectUri, ct);
    }

    public Task<OAuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, ct);

    private Task<OAuthTokenResponse> ExchangeCodeForTokenAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct) =>
        PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        }, ct);

    private async Task<OAuthTokenResponse> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        form["client_id"] = _options.ClientId;
        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            form["client_secret"] = _options.ClientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenUrl) { Content = new FormUrlEncodedContent(form) };
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        LogTokenRequest(_logger, _options.OAuthTokenUrl, form["grant_type"], (int)response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            throw new INaturalistApiException("OAuth token request", response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<OAuthTokenResponse>(body)
            is { AccessToken.Length: > 0 } token
            ? token
            : throw new InvalidOperationException("The OAuth token endpoint returned no access_token.");
    }

    /// <summary>Blocks until the redirect with the authorization code arrives, then shows a short confirmation page.</summary>
    private static async Task<string> WaitForAuthorizationCodeAsync(HttpListener listener, string expectedState, CancellationToken ct)
    {
        using var registration = ct.Register(listener.Stop);
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        var query = context.Request.QueryString;
        var error = query["error"];
        var code = query["code"];
        var valid = string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code) && query["state"] == expectedState;

        var html = valid
            ? "<html><body><h1>Erfolgreich angemeldet / Signed in</h1><p>Du kannst dieses Fenster schließen. / You can close this window.</p></body></html>"
            : $"<html><body><h1>Anmeldung fehlgeschlagen / Login failed</h1><p>{WebUtility.HtmlEncode(error ?? "invalid response")}</p></body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.Close();

        if (!string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException($"Authorization was denied or failed: {error}");
        }

        return valid ? code! : throw new InvalidOperationException("Invalid authorization response (missing code or wrong state parameter).");
    }

    private static Task OpenSystemBrowserAsync(Uri url, CancellationToken ct)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Best effort (e.g. headless environment); the host can supply its own AuthorizationPrompt.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Binds the loopback listener on the configured port. If that port is taken (another login session,
    /// another program), retries with OS-assigned free ports and returns the redirect URI that matches
    /// the port actually bound; authorize request and token exchange must both use it. RFC 8252 says
    /// authorization servers must accept any port for a loopback redirect, and iNaturalist ignores
    /// the port when matching.
    /// </summary>
    private HttpListener StartListener(string configuredRedirectUri, out string redirectUri)
    {
        var preferred = new Uri(configuredRedirectUri);
        HttpListenerException? lastError = null;
        for (var attempt = 0; attempt <= MaxFallbackPorts; attempt++)
        {
            var candidate = attempt == 0
                ? preferred
                : new UriBuilder(preferred) { Port = GetFreeLoopbackPort() }.Uri;
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(BuildListenerPrefix(candidate.AbsoluteUri));
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                // Typically "address already in use"; the free port found above can also be taken again before we bind it.
                listener.Close();
                lastError = ex;
                LogCannotListen(_logger, candidate.Port, ex.Message);
                continue;
            }

            if (attempt > 0)
            {
                LogFallbackPort(_logger, preferred.Port, candidate.Port);
            }

            redirectUri = candidate.AbsoluteUri;
            return listener;
        }

        throw new InvalidOperationException(
            $"The OAuth login cannot listen on port {preferred.Port} or on {MaxFallbackPorts} alternative loopback ports.", lastError);
    }

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>
    /// The listener prefix is the root of the redirect URI's host and port. The exact callback path
    /// is not bound: HttpListener needs a trailing slash for an exact-path prefix and then never
    /// matches the slash-less ".../callback" a real redirect uses (observed on Linux). The host must
    /// be spelled identically in redirect URI and prefix, since HttpListener matches the Host header text verbatim.
    /// </summary>
    internal static string BuildListenerPrefix(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}/";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
