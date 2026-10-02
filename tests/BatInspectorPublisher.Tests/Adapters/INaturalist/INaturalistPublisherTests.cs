using System.Net;
using System.Text.Json;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistPublisherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("inat-test-").FullName;
    private readonly StubHttpHandler _http = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private INaturalistPublisher CreatePublisher() =>
        new(TestData.Options(), new HttpClient(_http), _ => Task.FromResult("jwt-token"));

    private ObservationCandidate Candidate() => TestData.Candidate(
        spectrogram: WriteFile("s.png"), audio: WriteFile("a.wav"));

    private string WriteFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "data");
        return path;
    }

    private StubHttpHandler HappyPath() => _http
        .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK,
            """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
        .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0, "results": [] }""")
        .On("POST /v2/observations", HttpStatusCode.OK, """{ "total_results": 1, "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
        .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
        .On("POST /v2/observation_sounds", HttpStatusCode.OK, "{}");

    [Fact]
    public async Task Publish_Commit_CreatesObservationAndAttachesBothEvidenceFiles()
    {
        HappyPath();

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Created, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.Equal("https://www.inaturalist.org/observations/123", result.Url);
        Assert.True(result.SpectrogramAttached);
        Assert.True(result.AudioAttached);
        Assert.Equal(
            ["GET /v2/taxa/autocomplete", "GET /v1/observations", "POST /v2/observations", "POST /v2/observation_photos", "POST /v2/observation_sounds"],
            _http.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}"));
        Assert.All(_http.Requests, r => Assert.Equal("jwt-token", r.BearerToken));
    }

    [Fact]
    public async Task Publish_DefaultOptions_IsDryRunAndWritesNothing()
    {
        HappyPath();

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions());

        Assert.Equal(PublishStatus.WouldCreate, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_UnknownTaxon_IsSkippedAndNeverFallsBackToFirstHit()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK,
            """{ "results": [ { "id": 5, "name": "Pipistrellus kuhlii", "rank": "species" } ] }""");

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_ExistingObservation_IsSkippedAsDuplicate()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 1, "results": [ { "id": 1 } ] }""");

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_DuplicateCheck_UsesSingleDayAndInvariantCultureNumbers()
    {
        HappyPath();

        await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        var query = _http.Requests.Single(r => r.PathAndQuery.StartsWith("/v1/observations")).PathAndQuery;
        Assert.Contains("taxon_id=99", query);
        Assert.Contains("d1=2026-06-11&d2=2026-06-11", query);
        Assert.Contains("lat=50.11&lng=8.682", query);
        Assert.Contains("radius=0.1", query);
        Assert.Contains("mine_only=true", query);
    }

    [Fact]
    public async Task Publish_AudioUploadFails_ReportsPartialFailureWithObservationId()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0 }""")
            .On("POST /v2/observations", HttpStatusCode.OK, """{ "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
            .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
            .On("POST /v2/observation_sounds", HttpStatusCode.InternalServerError, "kaputt");

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.False(result.AudioAttached);
        Assert.IsType<INaturalistApiException>(result.Error);
        Assert.Contains("already created", result.Message);
    }

    [Fact]
    public async Task Publish_ApiError_BecomesFailedResult()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.Unauthorized, "nope");

        var result = await CreatePublisher().PublishAsync(Candidate(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, ((INaturalistApiException)result.Error!).StatusCode);
    }

    [Fact]
    public async Task Publish_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var publisher = new INaturalistPublisher(TestData.Options(), new HttpClient(_http), ct => Task.FromCanceled<string>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(Candidate(), new PublishOptions(), cts.Token));
    }

    [Fact]
    public async Task Publish_ResolvedTaxonIsCachedAcrossCandidates()
    {
        HappyPath();
        var publisher = CreatePublisher();

        await publisher.PublishAsync(Candidate(), new PublishOptions());
        await publisher.PublishAsync(Candidate(), new PublishOptions());

        Assert.Single(_http.Requests, r => r.PathAndQuery.StartsWith("/v2/taxa/autocomplete"));
    }

    [Fact]
    public void BuildPayload_SerializedJson_DoesNotContainTimeObservedAt()
    {
        var json = JsonSerializer.Serialize(CreatePublisher().BuildPayload(TestData.Candidate(), 123));

        Assert.DoesNotContain("time_observed_at", json);
    }

    [Fact]
    public void BuildPayload_PlaceGuess_IsAlwaysANonNullString()
    {
        var payload = CreatePublisher().BuildPayload(TestData.Candidate(), 123);

        Assert.Equal("50.11000, 8.68200", payload.PlaceGuess);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("place_guess").ValueKind);
    }

    [Fact]
    public void BuildPayload_UsesDateOnlyAndCandidateData()
    {
        var payload = CreatePublisher().BuildPayload(TestData.Candidate(), 99);

        Assert.Equal(99, payload.TaxonId);
        Assert.Equal("2026-06-11", payload.ObservedOnString);
        Assert.Equal("Pipistrellus pipistrellus", payload.SpeciesGuess);
        Assert.Equal("bat,acoustic-monitoring,batinspector", payload.TagList);
    }

    [Fact]
    public void BuildPayload_Description_ContainsMeasurementsInGermanFormatAndComment()
    {
        var description = CreatePublisher().BuildPayload(TestData.Candidate(comment: "unsicher"), 99).Description!;

        Assert.Contains("11.06.2026 21:37:49", description);
        Assert.Contains("Temperatur: 18,9 °C.", description);
        Assert.Contains("Luftfeuchte: 73,5 %.", description);
        Assert.Contains("Anmerkung zur Bestimmung: unsicher", description);
        Assert.StartsWith(TestData.Options().DescriptionPrefix, description);
    }

    [Fact]
    public void BuildPayload_Description_OmitsMissingOptionalParts()
    {
        var candidate = TestData.Candidate() with { TemperatureCelsius = null, HumidityPercent = null };

        var description = CreatePublisher().BuildPayload(candidate, 99).Description!;

        Assert.DoesNotContain("Temperatur", description);
        Assert.DoesNotContain("Luftfeuchte", description);
        Assert.DoesNotContain("Anmerkung", description);
    }
}
