using System.Net;
using BatInspectorPublisher.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistAuthenticatorTests
{
    private readonly StubHttpHandler _http = new();
    private readonly InMemoryTokenStore _store = new();

    private INaturalistAuthenticator Create(AuthorizationPrompt? prompt = null, INaturalistOptions? options = null) =>
        new(options ?? TestData.Options(), new HttpClient(_http), _store, prompt);

    private static INaturalistToken Token(DateTimeOffset obtainedAt, string? refresh = "refresh") => new()
    {
        AccessToken = "stored-jwt",
        RefreshToken = refresh,
        Username = "bat-fan",
        ObtainedAtUtc = obtainedAt,
        ExpiresInSeconds = 24 * 3600,
    };

    [Fact]
    public async Task EnsureAuthenticated_ValidStoredToken_IsReturnedWithoutNetwork()
    {
        _store.Token = Token(DateTimeOffset.UtcNow);

        Assert.Equal("stored-jwt", await Create().EnsureAuthenticatedAsync());
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task EnsureAuthenticated_ExpiredWithRefreshToken_RefreshesExchangesAndStores()
    {
        _store.Token = Token(DateTimeOffset.UtcNow.AddDays(-2));
        _http
            .On("POST /oauth/token", HttpStatusCode.OK, """{ "access_token": "new-oauth" }""")
            .On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "new-jwt" }""");

        var jwt = await Create().EnsureAuthenticatedAsync();

        Assert.Equal("new-jwt", jwt);
        Assert.Equal("new-jwt", _store.Token!.AccessToken);
        Assert.Equal("refresh", _store.Token.RefreshToken); // not rotated -> old one kept
        Assert.Equal("bat-fan", _store.Token.Username);
        Assert.Equal("new-oauth", _http.Requests.Single(r => r.Uri.AbsolutePath == "/users/api_token").BearerToken);
    }

    [Fact]
    public async Task EnsureAuthenticated_NoStoredToken_RunsInteractiveLoginAndStoresUsername()
    {
        var redirect = $"http://127.0.0.1:{TestData.FreeTcpPort()}/callback";
        _http
            .On("POST /oauth/token", HttpStatusCode.OK, """{ "access_token": "oauth", "refresh_token": "r" }""")
            .On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "login-jwt" }""")
            .On("GET /v1/users/me", HttpStatusCode.OK, """{ "results": [ { "login": "bat-fan" } ] }""");
        var prompted = false;
        var auth = Create((url, ct) =>
        {
            prompted = true;
            var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"];
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                using var client = new HttpClient();
                await client.GetAsync($"{redirect}?code=c&state={state}");
            });
            return Task.CompletedTask;
        }, TestData.Options(redirect));

        var jwt = await auth.EnsureAuthenticatedAsync();

        Assert.True(prompted);
        Assert.Equal("login-jwt", jwt);
        Assert.Equal("bat-fan", auth.StoredUsername);
        Assert.Equal(24 * 3600, _store.Token!.ExpiresInSeconds);
        Assert.Equal("oauth", _store.Token.OAuthAccessToken); // kept so later renewals need no browser
    }

    private static readonly AuthorizationPrompt NeverPrompt = (_, _) => throw new InvalidOperationException("The browser login must not be started.");

    [Fact]
    public async Task EnsureAuthenticated_ExpiredJwtWithStoredOAuthToken_RenewsSilentlyWithoutBrowser()
    {
        // iNaturalist OAuth tokens never expire and no refresh token is issued, so this is the normal daily case.
        _store.Token = Token(DateTimeOffset.UtcNow.AddDays(-2), refresh: null) with { OAuthAccessToken = "oauth-forever" };
        _http.On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "renewed-jwt" }""");

        var jwt = await Create(NeverPrompt).EnsureAuthenticatedAsync();

        Assert.Equal("renewed-jwt", jwt);
        var request = Assert.Single(_http.Requests);
        Assert.Equal("oauth-forever", request.BearerToken);
        Assert.Equal("renewed-jwt", _store.Token!.AccessToken);
        Assert.Equal("oauth-forever", _store.Token.OAuthAccessToken);
        Assert.Equal("bat-fan", _store.Token.Username);
        Assert.True(_store.Token.ObtainedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task EnsureAuthenticated_StoredOAuthTokenRevoked_FallsBackToLogin()
    {
        var redirect = $"http://127.0.0.1:{TestData.FreeTcpPort()}/callback";
        _store.Token = Token(DateTimeOffset.UtcNow.AddDays(-2), refresh: null) with { OAuthAccessToken = "revoked-oauth" };
        _http
            .On("POST /oauth/token", HttpStatusCode.OK, """{ "access_token": "new-oauth" }""")
            .On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "fresh-jwt" }""")
            .On("GET /v1/users/me", HttpStatusCode.OK, """{ "results": [ { "login": "bat-fan" } ] }""");
        var http = new HttpClient(new RejectBearer(_http, "revoked-oauth"));
        var auth = new INaturalistAuthenticator(TestData.Options(redirect), http, _store, (url, ct) =>
        {
            var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"];
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                using var client = new HttpClient();
                await client.GetAsync($"{redirect}?code=c&state={state}");
            });
            return Task.CompletedTask;
        });

        Assert.Equal("fresh-jwt", await auth.EnsureAuthenticatedAsync());
        Assert.Equal("new-oauth", _store.Token!.OAuthAccessToken);
    }

    [Fact]
    public async Task EnsureAuthenticated_NetworkErrorWhileRenewing_PropagatesWithoutOpeningBrowser()
    {
        var stored = Token(DateTimeOffset.UtcNow.AddDays(-2), refresh: null) with { OAuthAccessToken = "oauth-forever" };
        _store.Token = stored;
        var http = new HttpClient(new ThrowingHandler());

        var auth = new INaturalistAuthenticator(TestData.Options(), http, _store, NeverPrompt);

        await Assert.ThrowsAsync<HttpRequestException>(() => auth.EnsureAuthenticatedAsync());
        Assert.Equal(stored, _store.Token);
    }

    private sealed class RejectBearer(HttpMessageHandler inner, string rejectedToken) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Headers.Authorization?.Parameter == rejectedToken
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("revoked") })
                : base.SendAsync(request, cancellationToken);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No route to host");
    }

    [Fact]
    public async Task EnsureAuthenticated_RefreshRejected_FallsBackToLogin()
    {
        var redirect = $"http://127.0.0.1:{TestData.FreeTcpPort()}/callback";
        _store.Token = Token(DateTimeOffset.UtcNow.AddDays(-2));
        var tokenCalls = 0;
        _http
            .On("POST /oauth/token", HttpStatusCode.OK, """{ "access_token": "oauth" }""")
            .On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "fresh-jwt" }""")
            .On("GET /v1/users/me", HttpStatusCode.OK, """{ "results": [ { "login": "bat-fan" } ] }""");
        var failingHttp = new HttpClient(new FailFirstTokenCall(_http, () => tokenCalls++));
        var auth = new INaturalistAuthenticator(TestData.Options(redirect), failingHttp, _store, (url, ct) =>
        {
            var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"];
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                using var client = new HttpClient();
                await client.GetAsync($"{redirect}?code=c&state={state}");
            });
            return Task.CompletedTask;
        });

        Assert.Equal("fresh-jwt", await auth.EnsureAuthenticatedAsync());
        Assert.Equal(2, tokenCalls);
    }

    private sealed class FailFirstTokenCall(HttpMessageHandler inner, Action onTokenCall) : DelegatingHandler(inner)
    {
        private bool _failed;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/oauth/token")
            {
                onTokenCall();
                if (!_failed)
                {
                    _failed = true;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("invalid_grant") });
                }
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public void Constructor_MissingClientId_Throws()
    {
        Assert.Throws<ArgumentException>(() => Create(options: new INaturalistOptions { ClientId = " " }));
    }

    [Fact]
    public void Constructor_NonHttpRedirectUri_Throws()
    {
        Assert.Throws<ArgumentException>(() => Create(options: new INaturalistOptions { ClientId = "x", RedirectUri = "myapp://callback" }));
    }

    [Fact]
    public void Logout_ClearsStoredToken()
    {
        _store.Token = Token(DateTimeOffset.UtcNow);
        var auth = Create();
        Assert.True(auth.HasStoredToken);

        auth.Logout();

        Assert.False(auth.HasStoredToken);
    }

    [Fact]
    public void Token_IsLikelyExpired_FiveMinutesBeforeExpiry()
    {
        var token = Token(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var expiry = token.ExpiresAtUtc;

        Assert.False(token.IsLikelyExpired(expiry - TimeSpan.FromMinutes(6)));
        Assert.True(token.IsLikelyExpired(expiry - TimeSpan.FromMinutes(4)));
    }
}
