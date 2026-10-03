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

    private async Task<TaxonLookup> Resolve(string name, string rank, string responseBody)
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, responseBody);
        return await Client().ResolveTaxonAsync(name, rank, "jwt", default);
    }

    [Fact]
    public async Task ResolveTaxon_MatchesNameCaseInsensitively()
    {
        var lookup = await Resolve("pipistrellus pipistrellus", "species",
            """{ "results": [ { "id": 1, "name": "Pipistrellus", "rank": "genus" }, { "id": 2, "name": "Pipistrellus Pipistrellus", "rank": "species" } ] }""");

        Assert.Equal(2, lookup.Taxon!.Id);
    }

    [Fact]
    public async Task ResolveTaxon_GenusName_MatchesGenusEntry()
    {
        var lookup = await Resolve("Myotis", "genus",
            """{ "results": [ { "id": 1, "name": "Myotis", "rank": "genus" }, { "id": 2, "name": "Myotis myotis", "rank": "species" } ] }""");

        Assert.Equal(1, lookup.Taxon!.Id);
    }

    [Fact]
    public async Task ResolveTaxon_NoExactMatch_ReportsNotFound()
    {
        var lookup = await Resolve("Nyctaloid", "genus", """{ "results": [ { "id": 1, "name": "Something else", "rank": "genus" } ] }""");

        Assert.Null(lookup.Taxon);
        Assert.Contains("not found", lookup.Problem);
    }

    [Fact]
    public async Task ResolveTaxon_WrongRank_IsRejected()
    {
        // A one-word name that is only a family or order on iNaturalist is no genus.
        var lookup = await Resolve("Vespertilionidae", "genus", """{ "results": [ { "id": 1, "name": "Vespertilionidae", "rank": "family" } ] }""");

        Assert.Null(lookup.Taxon);
        Assert.Contains("family", lookup.Problem);
    }

    [Fact]
    public async Task ResolveTaxon_MissingRank_IsRejected()
    {
        var lookup = await Resolve("Myotis", "genus", """{ "results": [ { "id": 1, "name": "Myotis" } ] }""");

        Assert.Null(lookup.Taxon);
    }

    [Fact]
    public async Task ResolveTaxon_InactiveTaxon_IsRejected()
    {
        var lookup = await Resolve("Myotis oxygnathus", "species",
            """{ "results": [ { "id": 1, "name": "Myotis oxygnathus", "rank": "species", "is_active": false } ] }""");

        Assert.Null(lookup.Taxon);
    }

    [Fact]
    public async Task ResolveTaxon_SeveralMatches_IsAmbiguousAndPicksNone()
    {
        var lookup = await Resolve("Plecotus", "genus",
            """{ "results": [ { "id": 1, "name": "Plecotus", "rank": "genus" }, { "id": 2, "name": "Plecotus", "rank": "genus" } ] }""");

        Assert.Null(lookup.Taxon);
        Assert.Contains("ambiguous", lookup.Problem);
        Assert.Contains("1, 2", lookup.Problem);
    }

    [Fact]
    public async Task ResolveTaxon_SameTaxonListedTwice_IsNotAmbiguous()
    {
        var lookup = await Resolve("Myotis", "genus",
            """{ "results": [ { "id": 1, "name": "Myotis", "rank": "genus" }, { "id": 1, "name": "Myotis", "rank": "genus" } ] }""");

        Assert.Equal(1, lookup.Taxon!.Id);
    }

    [Fact]
    public async Task ResolveTaxon_MatchedOnlyAsSynonym_NamesTheCurrentName()
    {
        var lookup = await Resolve("Pipistrellus pygmaeus", "species",
            """{ "results": [ { "id": 7, "name": "Pipistrellus pygmaeus pygmaeus", "rank": "species", "matched_term": "Pipistrellus pygmaeus" } ] }""");

        Assert.Null(lookup.Taxon);
        Assert.Contains("lists it as 'Pipistrellus pygmaeus pygmaeus'", lookup.Problem);
    }

    [Fact]
    public async Task ResolveTaxon_EscapesTheQuery_AndAsksForRankActivityAndMatchedTerm()
    {
        await Resolve("Pipistrellus pipistrellus", "species", """{ "results": [] }""");

        var query = _http.Requests.Single().PathAndQuery;
        Assert.Contains("q=Pipistrellus%20pipistrellus", query);
        Assert.Contains("fields=id,name,rank,is_active,matched_term", query);
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
    public async Task Upload_SendsMultipartWithObservationUuidFileNameAndContent()
    {
        _http.On("POST /v2/observation_photos", HttpStatusCode.OK, "{}");

        await Client().AttachPhotoAsync("uuid-9", TestData.Evidence().Spectrogram, "jwt", default);

        var body = _http.Requests.Single().Body;
        Assert.Contains("observation_photo[observation_id]", body);
        Assert.Contains("uuid-9", body);
        Assert.Contains("filename=spectrogram.png", body);
        Assert.Contains("SPECTROGRAM-BYTES", body);
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
