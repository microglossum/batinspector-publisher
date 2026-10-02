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
    public async Task Authorize_WrongState_Throws()
    {
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(new StubHttpHandler()),
            Browser(_ => "code=the-code&state=forged"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.AuthorizeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Authorize_UserDenies_ThrowsWithProviderError()
    {
        var flow = new OAuthFlow(TestData.Options(NewRedirectUri()), new HttpClient(new StubHttpHandler()),
            Browser(q => $"error=access_denied&state={q["state"]}"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => flow.AuthorizeAsync(CancellationToken.None));

        Assert.Contains("access_denied", ex.Message);
    }

    [Fact]
    public async Task Authorize_NobodyCompletesLogin_TimesOut()
    {
        var options = new INaturalistOptions { ClientId = "c", RedirectUri = NewRedirectUri(), AuthorizationTimeout = TimeSpan.FromMilliseconds(200) };
        var flow = new OAuthFlow(options, new HttpClient(new StubHttpHandler()), (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<TimeoutException>(() => flow.AuthorizeAsync(CancellationToken.None));
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
