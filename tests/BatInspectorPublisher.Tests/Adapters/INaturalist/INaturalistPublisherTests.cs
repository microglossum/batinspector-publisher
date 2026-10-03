using System.Net;
using System.Text.Json;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistPublisherTests
{
    private readonly StubHttpHandler _http = new();

    private INaturalistPublisher CreatePublisher() =>
        new(TestData.Options(), new HttpClient(_http), _ => Task.FromResult("jwt-token"));

    private static ObservationCandidate Candidate() => TestData.Candidate();

    private static EvidenceFiles Evidence() => TestData.Evidence();

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

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

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

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions());

        Assert.Equal(PublishStatus.WouldCreate, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_UnknownTaxon_IsSkippedAndNeverFallsBackToFirstHit()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK,
            """{ "results": [ { "id": 5, "name": "Pipistrellus kuhlii", "rank": "species" } ] }""");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    private void ResolvesTo(string name, string rank, int id) => _http
        .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, $$"""{ "results": [ { "id": {{id}}, "name": "{{name}}", "rank": "{{rank}}" } ] }""")
        .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0, "results": [] }""")
        .On("POST /v2/observations", HttpStatusCode.OK, """{ "total_results": 1, "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
        .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
        .On("POST /v2/observation_sounds", HttpStatusCode.OK, "{}");

    [Theory]
    [InlineData("Nyctaloid", "Chiroptera", "order")]
    [InlineData("Social", "Chiroptera", "order")]
    [InlineData("?", "Chiroptera", "order")]
    [InlineData("Mbart", "Myotis", "genus")]
    public async Task Publish_UncertainCall_IsFiledUnderTheBroaderTaxonAndKeepsTheOriginalAsGuess(string call, string taxon, string rank)
    {
        ResolvesTo(taxon, rank, 42);

        var result = await CreatePublisher().PublishAsync(Candidate() with { ScientificName = call }, Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Created, result.Status);
        Assert.Equal(taxon, result.TaxonName);
        Assert.Contains($"q={Uri.EscapeDataString(taxon)}", _http.Requests[0].PathAndQuery);
        using var doc = JsonDocument.Parse(_http.Requests.Single(r => r.Method == "POST" && r.Uri.AbsolutePath == "/v2/observations").Body);
        var observation = doc.RootElement.GetProperty("observation");
        Assert.Equal(42, observation.GetProperty("taxon_id").GetInt32());
        Assert.Equal(call, observation.GetProperty("species_guess").GetString());
    }

    [Fact]
    public async Task Publish_UncertainCall_DryRunSaysItIsFiledUnderTheBroaderTaxon()
    {
        ResolvesTo("Chiroptera", "order", 42);

        var result = await CreatePublisher().PublishAsync(Candidate() with { ScientificName = "Nyctaloid" }, Evidence(), new PublishOptions());

        Assert.Equal(PublishStatus.WouldCreate, result.Status);
        Assert.Equal("Chiroptera", result.TaxonName);
        Assert.Contains("Nyctaloid as Chiroptera", result.Message);
    }

    [Fact]
    public async Task Publish_ExactMatch_ReportsTheTaxonName()
    {
        HappyPath();

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions());

        Assert.Equal("Pipistrellus pipistrellus", result.TaxonName);
        Assert.DoesNotContain(" as ", result.Message);
    }

    [Fact]
    public async Task Publish_UncertainCall_WhenTheBroaderTaxonIsNotFound_IsSkipped()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [] }""");

        var result = await CreatePublisher().PublishAsync(Candidate() with { ScientificName = "Nyctaloid" }, Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.Null(result.TaxonName);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData("todo")]
    [InlineData("TTEN")]
    [InlineData("Eptesicus Serotinuss")]
    public async Task Publish_NameThatIsNoTaxonAndNoKnownCall_IsSkippedNotFiledUnderBats(string name)
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [] }""");

        var result = await CreatePublisher().PublishAsync(Candidate() with { ScientificName = name }, Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.Single(_http.Requests);
        Assert.Contains($"q={Uri.EscapeDataString(name)}", _http.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task Publish_NameWithMoreThanTwoWords_IsSkippedWithoutAskingThePlatform()
    {
        var result = await CreatePublisher().PublishAsync(Candidate() with { ScientificName = "Myotis cf. daubentonii" }, Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Publish_ExistingObservation_IsSkippedAsDuplicate()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 1, "results": [ { "id": 1 } ] }""");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_DuplicateCheck_UsesSingleDayAndInvariantCultureNumbers()
    {
        HappyPath();

        await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        var query = _http.Requests.Single(r => r.PathAndQuery.StartsWith("/v1/observations")).PathAndQuery;
        Assert.Contains("taxon_id=99", query);
        Assert.Contains("d1=2026-06-11&d2=2026-06-11", query);
        Assert.Contains("lat=50.11&lng=8.682", query);
        Assert.Contains("radius=0.1", query);
        Assert.Contains("mine_only=true", query);
    }

    [Fact]
    public async Task Publish_DuplicateCheck_UsesTheLocalDayForANightAfterMidnight()
    {
        HappyPath();
        var candidate = Candidate() with { ObservedAt = new DateTimeOffset(2026, 6, 12, 0, 30, 0, TimeSpan.FromHours(2)) };

        await CreatePublisher().PublishAsync(candidate, Evidence(), new PublishOptions { Commit = true });

        // 22:30 UTC on the 11th, but the 12th in Berlin.
        Assert.Contains("d1=2026-06-12&d2=2026-06-12", _http.Requests.Single(r => r.PathAndQuery.StartsWith("/v1/observations")).PathAndQuery);
    }

    [Fact]
    public async Task Publish_Commit_UploadsTheBytesItWasGiven()
    {
        HappyPath();

        await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Contains("SPECTROGRAM-BYTES", _http.Requests.Single(r => r.PathAndQuery.StartsWith("/v2/observation_photos")).Body);
        Assert.Contains("AUDIO-BYTES", _http.Requests.Single(r => r.PathAndQuery.StartsWith("/v2/observation_sounds")).Body);
    }

    [Fact]
    public async Task Publish_AudioUploadFails_ReportsPartialFailureWithObservationId()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0 }""")
            .On("POST /v2/observations", HttpStatusCode.OK, """{ "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
            .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
            .On("POST /v2/observation_sounds", HttpStatusCode.InternalServerError, "kaputt");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.False(result.AudioAttached);
        Assert.IsType<INaturalistApiException>(result.Error);
        Assert.Contains("already created", result.Message);
        Assert.Equal(PublishStep.AttachAudio, result.InterruptedStep);
    }

    [Fact]
    public async Task Publish_ApiError_BecomesFailedResult()
    {
        _http.On("GET /v2/taxa/autocomplete", HttpStatusCode.Unauthorized, "nope");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, ((INaturalistApiException)result.Error!).StatusCode);
    }

    [Fact]
    public async Task Publish_CancelledBeforeAnythingWasCreated_ReportsCancelledWithoutObservation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var publisher = new INaturalistPublisher(TestData.Options(), new HttpClient(_http), ct => Task.FromCanceled<string>(ct));

        var result = await publisher.PublishAsync(Candidate(), Evidence(), new PublishOptions(), cts.Token);

        Assert.Equal(PublishStatus.Cancelled, result.Status);
        Assert.Equal(PublishStep.Preparation, result.InterruptedStep);
        Assert.Null(result.ObservationId);
        Assert.Contains("nothing was created", result.Message);
    }

    [Fact]
    public async Task Publish_CancelledWhileAttachingAudio_KeepsWhatWasAlreadyCreated()
    {
        using var cts = new CancellationTokenSource();
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0 }""")
            .On("POST /v2/observations", HttpStatusCode.OK, """{ "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
            .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
            .On("POST /v2/observation_sounds", () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true }, cts.Token);

        Assert.Equal(PublishStatus.Cancelled, result.Status);
        Assert.Equal(PublishStep.AttachAudio, result.InterruptedStep);
        Assert.Equal("123", result.ObservationId);
        Assert.Equal("https://www.inaturalist.org/observations/123", result.Url);
        Assert.True(result.SpectrogramAttached);
        Assert.False(result.AudioAttached);
        Assert.Contains("already created", result.Message);
    }

    [Fact]
    public async Task Publish_CancelledWhileCreating_SaysTheOutcomeIsUnknown()
    {
        using var cts = new CancellationTokenSource();
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0 }""")
            .On("POST /v2/observations", () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true }, cts.Token);

        Assert.Equal(PublishStatus.Cancelled, result.Status);
        Assert.Equal(PublishStep.CreateObservation, result.InterruptedStep);
        Assert.Null(result.ObservationId);
        Assert.Contains("may or may not exist", result.Message);
    }

    [Fact]
    public async Task Publish_HttpTimeoutWithoutCallerCancellation_IsFailedNotCancelled()
    {
        _http.On("GET /v2/taxa/autocomplete", () => throw new TaskCanceledException("The request timed out."));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.Preparation, result.InterruptedStep);
        Assert.IsType<TaskCanceledException>(result.Error);
    }

    [Fact]
    public async Task Publish_CreateFails_NamesTheCreateStep()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, """{ "total_results": 0 }""")
            .On("POST /v2/observations", HttpStatusCode.InternalServerError, "kaputt");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.CreateObservation, result.InterruptedStep);
        Assert.Null(result.ObservationId);
    }

    [Fact]
    public async Task Publish_Created_LogsTheObservationIdAtInformation()
    {
        HappyPath();
        var logger = new CapturingLogger();
        var publisher = new INaturalistPublisher(TestData.Options(), new HttpClient(_http), _ => Task.FromResult("jwt-token"), logger);

        await publisher.PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information && e.Message.Contains("123"));
    }

    private string ExistingObservations(string photos, string sounds, string? description = null)
    {
        description ??= CreatePublisher().BuildPayload(Candidate(), 99).Description;
        return $$"""
            { "total_results": 1, "results": [ { "id": 123, "uuid": "uuid-1", "description": {{JsonSerializer.Serialize(description)}}, "photos": {{photos}}, "sounds": {{sounds}} } ] }
            """;
    }

    private StubHttpHandler WithExisting(string searchResponse) => _http
        .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
        .On("GET /v1/observations", HttpStatusCode.OK, searchResponse)
        .On("POST /v2/observation_photos", HttpStatusCode.OK, "{}")
        .On("POST /v2/observation_sounds", HttpStatusCode.OK, "{}");

    [Fact]
    public async Task Publish_OwnObservationWithoutSound_AttachesOnlyTheMissingSound()
    {
        WithExisting(ExistingObservations(photos: "[ { \"id\": 1 } ]", sounds: "[]"));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Resumed, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.True(result.AudioAttached);
        Assert.Equal(
            ["GET /v2/taxa/autocomplete", "GET /v1/observations", "POST /v2/observation_sounds"],
            _http.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}"));
        Assert.Contains("uuid-1", _http.Requests.Last().Body);
    }

    [Fact]
    public async Task Publish_OwnObservationWithoutAnyEvidence_AttachesBothAndCreatesNothing()
    {
        WithExisting(ExistingObservations(photos: "[]", sounds: "[]"));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Resumed, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.PathAndQuery.StartsWith("/v2/observations"));
        Assert.Single(_http.Requests, r => r.PathAndQuery.StartsWith("/v2/observation_photos"));
        Assert.Single(_http.Requests, r => r.PathAndQuery.StartsWith("/v2/observation_sounds"));
    }

    [Fact]
    public async Task Publish_ResumeFails_ReportsTheStepAndKeepsTheObservationId()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On("GET /v1/observations", HttpStatusCode.OK, ExistingObservations(photos: "[]", sounds: "[]"))
            .On("POST /v2/observation_photos", HttpStatusCode.InternalServerError, "kaputt");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.AttachSpectrogram, result.InterruptedStep);
        Assert.Equal("123", result.ObservationId);
        Assert.False(result.SpectrogramAttached);
    }

    [Fact]
    public async Task Publish_OwnObservationWithBothEvidenceFiles_IsADuplicateAndReportsTheExistingObservation()
    {
        WithExisting(ExistingObservations(photos: "[ { \"id\": 1 } ]", sounds: "[ { \"id\": 2 } ]"));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.True(result.SpectrogramAttached);
        Assert.True(result.AudioAttached);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_IncompleteObservationWithADifferentDescription_IsLeftAlone()
    {
        WithExisting(ExistingObservations(photos: "[]", sounds: "[]", description: "Selbst eingetragen, ohne Belege."));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.Equal("123", result.ObservationId);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Publish_IncompleteObservationIgnoringWhitespaceDifferences_IsStillRecognized()
    {
        var description = CreatePublisher().BuildPayload(Candidate(), 99).Description!.Replace(" ", "  ") + "\n";
        WithExisting(ExistingObservations(photos: "[]", sounds: "[]", description: description));

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Resumed, result.Status);
    }

    [Fact]
    public async Task Publish_ExistingObservationWithUnreportedMedia_IsNeverTouched()
    {
        var description = JsonSerializer.Serialize(CreatePublisher().BuildPayload(Candidate(), 99).Description);
        WithExisting($$"""{ "total_results": 1, "results": [ { "id": 123, "uuid": "uuid-1", "description": {{description}} } ] }""");

        var result = await CreatePublisher().PublishAsync(Candidate(), Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedDuplicate, result.Status);
        Assert.DoesNotContain(_http.Requests, r => r.Method == "POST");
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
    public void BuildPayload_NightAfterMidnightInBerlin_UsesTheLocalDate()
    {
        // 22:30 UTC on 13.06. is 00:30 on 14.06. in Berlin; the platform must see the 14th.
        var candidate = TestData.Candidate() with { ObservedAt = new DateTimeOffset(2026, 6, 14, 0, 30, 0, TimeSpan.FromHours(2)) };

        Assert.Equal("2026-06-14", CreatePublisher().BuildPayload(candidate, 1).ObservedOnString);
    }

    [Theory]
    [InlineData(1, "MEZ")]
    [InlineData(2, "MESZ")]
    public void BuildPayload_Description_NamesTheGermanZone(int offsetHours, string zone)
    {
        var candidate = TestData.Candidate() with { ObservedAt = new DateTimeOffset(2026, 6, 11, 21, 37, 49, TimeSpan.FromHours(offsetHours)) };

        Assert.Contains($"21:37:49 Uhr {zone}.", CreatePublisher().BuildPayload(candidate, 1).Description);
    }

    [Fact]
    public void BuildPayload_Description_NamesAForeignZoneExplicitly()
    {
        var candidate = TestData.Candidate() with
        {
            TimeZoneId = "Europe/Lisbon",
            ObservedAt = new DateTimeOffset(2026, 6, 11, 21, 37, 49, TimeSpan.FromHours(1)),
        };

        var description = CreatePublisher().BuildPayload(candidate, 1).Description;

        Assert.Contains("21:37:49 Uhr (Zeitzone Europe/Lisbon, UTC+01:00).", description);
        Assert.DoesNotContain("MEZ", description);
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
    public void BuildPayload_Description_NamesTheOriginalCallWhenFiledUnderABroaderTaxon()
    {
        var candidate = Candidate() with { ScientificName = "Nyctaloid" };

        var description = CreatePublisher().BuildPayload(candidate, 42, "Chiroptera").Description!;

        Assert.Contains("Bestimmung in BatInspector: \"Nyctaloid\" (keine sichere Artbestimmung), hier als Chiroptera eingetragen.", description);
    }

    [Fact]
    public void BuildPayload_Description_HasNoBroaderTaxonLineForAnExactMatch()
    {
        var description = CreatePublisher().BuildPayload(Candidate(), 99, "Pipistrellus pipistrellus").Description!;

        Assert.DoesNotContain("Bestimmung in BatInspector", description);
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

internal sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
