using System.Net;
using System.Text.Json;
using BatInspectorPublisher.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistApiClientTests
{
    private readonly StubHttpHandler _http = new();

    private INaturalistApiClient Client() => new(TestData.Options(), new HttpClient(_http));

    [Fact]
    public void CreateObservationResponse_ResultsWrapper_ReturnsRealIdAndUuid()
    {
        var json = """{ "total_results": 1, "page": 1, "results": [ { "id": 123456789, "uuid": "abcd-1234-uuid" } ] }""";

        var result = Assert.Single(JsonSerializer.Deserialize<CreateObservationResponse>(json)!.Results);

        Assert.Equal(123456789, result.Id);
        Assert.Equal("abcd-1234-uuid", result.Uuid);
    }

    [Fact]
    public async Task CreateObservation_ResponseWithoutUuid_Throws()
    {
        _http.On("POST /v2/observations", HttpStatusCode.OK, """{ "results": [ { "id": 5 } ] }""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Client().CreateObservationAsync(new ObservationPayload(), "jwt", default));
    }

    [Fact]
    public async Task CreateObservation_SendsWrappedPayloadWithoutNulls()
    {
        _http.On("POST /v2/observations", HttpStatusCode.OK, """{ "results": [ { "id": 5, "uuid": "u" } ] }""");

        await Client().CreateObservationAsync(new ObservationPayload { TaxonId = 7, ObservedOnString = "2026-06-11", PlaceGuess = "x" }, "jwt", default);

        using var doc = JsonDocument.Parse(_http.Requests.Single().Body);
        var observation = doc.RootElement.GetProperty("observation");
        Assert.Equal(7, observation.GetProperty("taxon_id").GetInt32());
        Assert.False(observation.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task ResolveTaxon_MatchesNameCaseInsensitively()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK,
            """{ "results": [ { "id": 1, "name": "Pipistrellus" }, { "id": 2, "name": "Pipistrellus Pipistrellus" } ] }""");

        var taxon = await Client().ResolveTaxonAsync("pipistrellus pipistrellus", "jwt", default);

        Assert.Equal(2, taxon!.Id);
    }

    [Fact]
    public async Task ResolveTaxon_GenusName_MatchesGenusEntry()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK,
            """{ "results": [ { "id": 1, "name": "Myotis", "rank": "genus" }, { "id": 2, "name": "Myotis myotis" } ] }""");

        Assert.Equal(1, (await Client().ResolveTaxonAsync("Myotis", "jwt", default))!.Id);
    }

    [Fact]
    public async Task ResolveTaxon_NoExactMatch_ReturnsNull()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 1, "name": "Something else" } ] }""");

        Assert.Null(await Client().ResolveTaxonAsync("Nyctaloid", "jwt", default));
    }

    [Fact]
    public async Task ResolveTaxon_EscapesTheQuery()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [] }""");

        await Client().ResolveTaxonAsync("Pipistrellus pipistrellus", "jwt", default);

        Assert.Contains("q=Pipistrellus%20pipistrellus", _http.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task ExchangeToken_ReturnsApiToken_AndSendsOAuthTokenAsBearer()
    {
        _http.On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "the-jwt" }""");

        Assert.Equal("the-jwt", await Client().ExchangeOAuthTokenForJwtAsync("oauth-token", default));
        Assert.Equal("oauth-token", _http.Requests.Single().BearerToken);
    }

    [Fact]
    public async Task ExchangeToken_ResponseWithoutToken_Throws()
    {
        _http.On("GET /users/api_token", HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Client().ExchangeOAuthTokenForJwtAsync("oauth-token", default));
    }

    [Fact]
    public async Task Upload_SendsMultipartWithObservationUuidAndFileName()
    {
        var file = Path.GetTempFileName();
        try
        {
            _http.On("POST /v2/observation_photos", HttpStatusCode.OK, "{}");

            await Client().AttachPhotoAsync("uuid-9", file, "jwt", default);

            var body = _http.Requests.Single().Body;
            Assert.Contains("observation_photo[observation_id]", body);
            Assert.Contains("uuid-9", body);
            Assert.Contains($"filename={Path.GetFileName(file)}", body);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ErrorResponse_ThrowsApiExceptionWithStatusAndBody()
    {
        _http.On("GET /v1/users/me", HttpStatusCode.Unauthorized, "bad token");

        var ex = await Assert.ThrowsAsync<INaturalistApiException>(() => Client().GetAuthenticatedUsernameAsync("jwt", default));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("bad token", ex.ResponseBody);
    }
}
