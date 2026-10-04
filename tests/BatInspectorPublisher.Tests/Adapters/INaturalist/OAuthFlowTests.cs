using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using BatInspectorPublisher.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class OAuthFlowTests
{
    private static string NewRedirectUri() => $"http://127.0.0.1:{TestData.FreeTcpPort()}/callback";

    private static string TokenJson => """{ "access_token": "oauth-access", "refresh_token": "oauth-refresh" }""";

    /// <summary>A "browser" that follows the authorize URL by calling the redirect URI back.</summary>
    private static AuthorizationPrompt Browser(Func<System.Collections.Specialized.NameValueCollection, string> callbackQuery, Action<Uri>? onUrl = null) =>
        (url, ct) =>
        {
            onUrl?.Invoke(url);
            var query = HttpUtility.ParseQueryString(url.Query);
            var callback = $"{query["redirect_uri"]}?{callbackQuery(query)}";
            // Fire the redirect after the prompt returns, like a real browser would.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50, CancellationToken.None);
                using var client = new HttpClient();
                await client.GetAsync(callback, CancellationToken.None);
            }, CancellationToken.None);
            return Task.CompletedTask;
        };

    [Fact]
    public async Task Authorize_HappyPath_SendsPkceParametersAndExchangesCode()
    {
        var redirect = NewRedirectUri();
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        Uri? authorizeUrl = null;
        var flow = new OAuthFlow(TestData.Options(redirect), new HttpClient(http),
            Browser(q => $"code=the-code&state={q["state"]}", url => authorizeUrl = url));

        var token = await flow.AuthorizeAsync(CancellationToken.None);

        Assert.Equal("oauth-access", token.AccessToken);
        Assert.Equal("oauth-refresh", token.RefreshToken);

        var query = HttpUtility.ParseQueryString(authorizeUrl!.Query);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal(redirect, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        var form = HttpUtility.ParseQueryString(http.Requests.Single().Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("test-secret", form["client_secret"]);
        Assert.Equal(redirect, form["redirect_uri"]);
        var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedChallenge, query["code_challenge"]);
    }

    [Fact]
    public async Task Authorize_ConfiguredPortInUse_FallsBackToFreePortAndUsesItConsistently()
    {
        var configured = NewRedirectUri();
        using var occupant = new HttpListener();
        occupant.Prefixes.Add(OAuthFlow.BuildListenerPrefix(configured));
        occupant.Start();
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        Uri? authorizeUrl = null;
        var flow = new OAuthFlow(TestData.Options(configured), new HttpClient(http),
            Browser(q => $"code=the-code&state={q["state"]}", url => authorizeUrl = url));

        var token = await flow.AuthorizeAsync(CancellationToken.None);

        Assert.Equal("oauth-access", token.AccessToken);
        var used = HttpUtility.ParseQueryString(authorizeUrl!.Query)["redirect_uri"];
        Assert.NotEqual(configured, used);
        Assert.EndsWith("/callback", used);
        Assert.Equal(used, HttpUtility.ParseQueryString(http.Requests.Single().Body)["redirect_uri"]);
    }

    [Fact]
    public async Task Authorize_ConfiguredPortFree_KeepsConfiguredRedirectUri()
    {
        var configured = NewRedirectUri();
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        Uri? authorizeUrl = null;
        var flow = new OAuthFlow(TestData.Options(configured), new HttpClient(http),
            Browser(q => $"code=c&state={q["state"]}", url => authorizeUrl = url));

        await flow.AuthorizeAsync(CancellationToken.None);

        Assert.Equal(configured, HttpUtility.ParseQueryString(authorizeUrl!.Query)["redirect_uri"]);
    }

    [Fact]
    public async Task Authorize_DenialWithoutState_StillEndsTheLogin()
    {
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(new StubHttpHandler()), Browser(_ => "error=access_denied"));

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.Denied, ex.Reason);
    }

    [Fact]
    public async Task Authorize_DenialWithForeignState_IsIgnored()
    {
        var options = new INaturalistOptions { ClientId = "c", RedirectUri = NewRedirectUri(), AuthorizationTimeout = TimeSpan.FromMilliseconds(500) };
        var flow = new OAuthFlow(options, new HttpClient(new StubHttpHandler()), Browser(_ => "error=access_denied&state=forged"));

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.TimedOut, ex.Reason);
    }

    [Fact]
    public async Task Authorize_NoPortCanBeOpened_ThrowsListenerUnavailable()
    {
        // An address that does not belong to this machine cannot be bound on any attempt, fallback ports included.
        var options = new INaturalistOptions { ClientId = "c", RedirectUri = "http://203.0.113.1:45679/callback" };
        var flow = new OAuthFlow(options, new HttpClient(new StubHttpHandler()), (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.ListenerUnavailable, ex.Reason);
    }

    [Fact]
    public async Task Authorize_PublicClient_OmitsClientSecret()
    {
        var redirect = NewRedirectUri();
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        var options = new INaturalistOptions { ClientId = "public-client", RedirectUri = redirect };
        var flow = new OAuthFlow(options, new HttpClient(http), Browser(q => $"code=c&state={q["state"]}"));

        await flow.AuthorizeAsync(CancellationToken.None);

        Assert.DoesNotContain("client_secret", http.Requests.Single().Body);
    }

    [Fact]
    public async Task Authorize_WrongStateOnly_IsIgnoredUntilTimeout()
    {
        var options = new INaturalistOptions { ClientId = "c", RedirectUri = NewRedirectUri(), AuthorizationTimeout = TimeSpan.FromMilliseconds(500) };
        var flow = new OAuthFlow(options, new HttpClient(new StubHttpHandler()), Browser(_ => "code=the-code&state=forged"));

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.TimedOut, ex.Reason);
    }

    [Fact]
    public async Task Authorize_StrayRequestsBeforeTheRealRedirect_DoNotEndTheLogin()
    {
        var redirect = NewRedirectUri();
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        var strayStatuses = new List<HttpStatusCode>();
        var flow = new OAuthFlow(TestData.Options(redirect), new HttpClient(http), (url, ct) =>
        {
            var state = HttpUtility.ParseQueryString(url.Query)["state"];
            var root = new Uri(redirect).GetLeftPart(UriPartial.Authority);
            _ = Task.Run(async () =>
            {
                await Task.Delay(50, CancellationToken.None);
                using var client = new HttpClient();
                foreach (var stray in new[] { "/", "/favicon.ico", "/callback?code=x&state=forged", "/callback?state=" + state })
                {
                    strayStatuses.Add((await client.GetAsync(root + stray, CancellationToken.None)).StatusCode);
                }

                await client.GetAsync($"{root}/callback?code=the-code&state={state}", CancellationToken.None);
            }, CancellationToken.None);
            return Task.CompletedTask;
        });

        var token = await flow.AuthorizeAsync(CancellationToken.None);

        Assert.Equal("oauth-access", token.AccessToken);
        Assert.All(strayStatuses, status => Assert.Equal(HttpStatusCode.NotFound, status));
        Assert.Equal(4, strayStatuses.Count);
        Assert.Equal("the-code", HttpUtility.ParseQueryString(http.Requests.Single().Body)["code"]);
    }

    [Fact]
    public async Task Authorize_UserDenies_ThrowsWithProviderError()
    {
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(new StubHttpHandler()),
            Browser(q => $"error=access_denied&state={q["state"]}"));

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.Denied, ex.Reason);
        Assert.Contains("access_denied", ex.Message);
    }

    [Fact]
    public async Task Authorize_NobodyCompletesLogin_TimesOut()
    {
        var options = new INaturalistOptions { ClientId = "c", RedirectUri = NewRedirectUri(), AuthorizationTimeout = TimeSpan.FromMilliseconds(200) };
        var flow = new OAuthFlow(options, new HttpClient(new StubHttpHandler()), (_, _) => Task.CompletedTask);

        var ex = await Assert.ThrowsAsync<INaturalistLoginException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(INaturalistLoginFailure.TimedOut, ex.Reason);
    }

    [Fact]
    public async Task Authorize_CallerCancels_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(new StubHttpHandler()), (_, _) => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.AuthorizeAsync(cts.Token));
    }

    [Fact]
    public async Task Authorize_TokenEndpointRejects_ThrowsApiException()
    {
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.BadRequest, "invalid_grant");
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(http), Browser(q => $"code=c&state={q["state"]}"));

        var ex = await Assert.ThrowsAsync<INaturalistApiException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Refresh_SendsRefreshGrant()
    {
        var http = new StubHttpHandler().On("POST /oauth/token", HttpStatusCode.OK, TokenJson);
        var flow = new OAuthFlow(TestData.Options(), new HttpClient(http));

        await flow.RefreshAsync("old-refresh", CancellationToken.None);

        var form = HttpUtility.ParseQueryString(http.Requests.Single().Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("old-refresh", form["refresh_token"]);
        Assert.Equal("test-client", form["client_id"]);
    }

    [Theory]
    [InlineData("http://127.0.0.1:45679/callback", "http://127.0.0.1:45679/")]
    [InlineData("http://127.0.0.1:8123/some/other/path", "http://127.0.0.1:8123/")]
    public void BuildListenerPrefix_BindsHostAndPortRoot(string redirect, string expected)
    {
        Assert.Equal(expected, OAuthFlow.BuildListenerPrefix(redirect));
    }
}
