using System.Net;
using System.Net.Http.Headers;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Results;
using Microsoft.Extensions.Logging;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

/// <summary>Retry and pacing of the iNaturalist API client. Time is fake: waits are recorded and advance a fake clock, so nothing sleeps.</summary>
public class INaturalistRetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _http = new();
    private readonly FakeClock _clock = new(Now);
    private readonly List<TimeSpan> _waits = [];

    private static INaturalistOptions Options(int maxAttempts = 3, double baseSeconds = 5, double maxSeconds = 60, double intervalSeconds = 0) => new()
    {
        ClientId = "test-client",
        MaxAttempts = maxAttempts,
        RetryBaseDelay = TimeSpan.FromSeconds(baseSeconds),
        MaxRetryDelay = TimeSpan.FromSeconds(maxSeconds),
        MinRequestInterval = TimeSpan.FromSeconds(intervalSeconds),
    };

    private Task Wait(TimeSpan wait, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _waits.Add(wait);
        _clock.Advance(wait);
        return Task.CompletedTask;
    }

    private INaturalistApiClient Client(INaturalistOptions? options = null, ILogger? logger = null, Func<TimeSpan, CancellationToken, Task>? wait = null) =>
        new(options ?? Options(), new HttpClient(_http), logger, _clock, wait ?? Wait);

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body = "{}", TimeSpan? retryAfter = null, DateTimeOffset? retryAfterDate = null) => () =>
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfter is { } delta)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        }

        if (retryAfterDate is { } date)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(date);
        }

        return response;
    };

    /// <summary>Answers the next request of this route with the next item; a throwing item simulates a network failure.</summary>
    private void Script(string route, params Func<HttpResponseMessage>[] answers)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(answers);
        _http.On(route, () => queue.Dequeue()());
    }

    private const string Observations = "GET /v1/observations";
    private const string Create = "POST /v2/observations";
    private const string Sound = "POST /v2/observation_sounds";

    private Task<ObservationsSearchResponse> Search(INaturalistApiClient client, CancellationToken ct = default) =>
        client.FindExistingObservationsAsync(7, new DateOnly(2026, 6, 11), 50.11, 8.682, "jwt", ct);

    private static Task Upload(INaturalistApiClient client) =>
        client.AttachSoundAsync("uuid-1", new BatInspectorPublisher.Core.Models.EvidenceFile("audio.wav", "AUDIO-BYTES"u8.ToArray()), "jwt", default);

    [Fact]
    public async Task Read_ServerError_IsRetriedUntilItSucceeds()
    {
        Script(Observations, Respond(HttpStatusCode.ServiceUnavailable, "busy"), Respond(HttpStatusCode.OK, """{ "total_results": 4 }"""));

        var result = await Search(Client());

        Assert.Equal(4, result.TotalResults);
        Assert.Equal(2, _http.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(5)], _waits);
    }

    [Fact]
    public async Task Read_KeepsFailing_BackoffDoublesIsCappedAndTheLastFailureIsThrown()
    {
        Script(Observations, Respond(HttpStatusCode.BadGateway), Respond(HttpStatusCode.BadGateway), Respond(HttpStatusCode.BadGateway), Respond(HttpStatusCode.GatewayTimeout, "last"));

        var ex = await Assert.ThrowsAsync<INaturalistApiException>(() => Search(Client(Options(maxAttempts: 4, baseSeconds: 5, maxSeconds: 8))));

        Assert.Equal(HttpStatusCode.GatewayTimeout, ex.StatusCode);
        Assert.Equal("last", ex.ResponseBody);
        Assert.Equal(4, _http.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8)], _waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task Read_OtherErrors_AreNotRetried(HttpStatusCode status)
    {
        Script(Observations, Respond(status));

        await Assert.ThrowsAsync<INaturalistApiException>(() => Search(Client()));

        Assert.Single(_http.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task MaxAttemptsOne_TurnsRetryingOff()
    {
        Script(Observations, Respond(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<INaturalistApiException>(() => Search(Client(Options(maxAttempts: 1))));

        Assert.Single(_http.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task Read_NetworkErrorAndTimeout_AreRetried()
    {
        Script(Observations,
            () => throw new HttpRequestException("connection reset"),
            () => throw new TaskCanceledException("timeout", new TimeoutException()),
            Respond(HttpStatusCode.OK, """{ "total_results": 1 }"""));

        var result = await Search(Client());

        Assert.Equal(1, result.TotalResults);
        Assert.Equal(3, _http.Requests.Count);
    }

    [Fact]
    public async Task Read_NetworkErrorOnEveryAttempt_ThrowsTheOriginalException()
    {
        Script(Observations, () => throw new HttpRequestException("one"), () => throw new HttpRequestException("two"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Search(Client(Options(maxAttempts: 2))));

        Assert.Equal("two", ex.Message);
    }

    [Fact]
    public async Task TooManyRequests_HonorsRetryAfterInsteadOfTheBackoff()
    {
        Script(Observations, Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(7)), Respond(HttpStatusCode.OK, """{ "total_results": 1 }"""));

        await Search(Client());

        Assert.Equal([TimeSpan.FromSeconds(7)], _waits);
    }

    [Fact]
    public async Task TooManyRequests_RetryAfterAsDate_IsMeasuredFromNow()
    {
        Script(Observations, Respond(HttpStatusCode.TooManyRequests, retryAfterDate: Now.AddSeconds(12)), Respond(HttpStatusCode.OK, """{ "total_results": 1 }"""));

        await Search(Client());

        Assert.Equal([TimeSpan.FromSeconds(12)], _waits);
    }

    [Fact]
    public async Task TooManyRequests_RetryAfterBeyondTheCap_FailsAtOnceAndReportsIt()
    {
        Script(Observations, Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMinutes(10)));

        var ex = await Assert.ThrowsAsync<INaturalistApiException>(() => Search(Client(Options(maxSeconds: 60))));

        Assert.Equal(TimeSpan.FromMinutes(10), ex.RetryAfter);
        Assert.Single(_http.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task Write_TooManyRequests_IsRetriedWithTheSameBody()
    {
        Script(Create, Respond(HttpStatusCode.TooManyRequests), Respond(HttpStatusCode.OK, """{ "results": [ { "id": 5, "uuid": "u" } ] }"""));

        var created = await Client().CreateObservationAsync(new ObservationPayload { TaxonId = 7, PlaceGuess = "x" }, "jwt", default);

        Assert.Equal("u", created.Uuid);
        Assert.Equal(2, _http.Requests.Count);
        Assert.NotEmpty(_http.Requests[0].Body);
        Assert.Equal(_http.Requests[0].Body, _http.Requests[1].Body);
    }

    [Fact]
    public async Task Upload_TooManyRequests_SendsTheFileAgain()
    {
        Script(Sound, Respond(HttpStatusCode.TooManyRequests), Respond(HttpStatusCode.OK));

        await Upload(Client());

        Assert.Equal(2, _http.Requests.Count);
        Assert.All(_http.Requests, r => Assert.Contains("AUDIO-BYTES", r.Body));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Write_ServerError_IsNotRepeated_BecauseTheServerMayHaveProcessedIt(HttpStatusCode status)
    {
        Script(Create, Respond(status), Respond(HttpStatusCode.OK, """{ "results": [ { "id": 5, "uuid": "u" } ] }"""));

        await Assert.ThrowsAsync<INaturalistApiException>(() => Client().CreateObservationAsync(new ObservationPayload(), "jwt", default));

        Assert.Single(_http.Requests);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task Write_NetworkErrorAfterSending_IsNotRepeated()
    {
        Script(Sound, () => throw new HttpRequestException("connection reset"), Respond(HttpStatusCode.OK));

        await Assert.ThrowsAsync<HttpRequestException>(() => Upload(Client()));

        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Write_Timeout_IsNotRepeated()
    {
        Script(Sound, () => throw new TaskCanceledException("timeout", new TimeoutException()), Respond(HttpStatusCode.OK));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Upload(Client()));

        Assert.Single(_http.Requests);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task Write_FailedConnectionSetup_IsRepeated_BecauseNothingWasSent(HttpRequestError error)
    {
        Script(Sound, () => throw new HttpRequestException(error, "no connection"), Respond(HttpStatusCode.OK));

        await Upload(Client());

        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Cancellation_DuringTheWait_EndsTheRetry()
    {
        Script(Observations, Respond(HttpStatusCode.ServiceUnavailable), Respond(HttpStatusCode.OK, """{ "total_results": 1 }"""));
        using var cts = new CancellationTokenSource();
        var client = Client(wait: (_, ct) =>
        {
            cts.Cancel();
            return Task.FromCanceled(ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Search(client, cts.Token));

        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Pacing_SpacesRequestsByTheMinimumInterval()
    {
        _http.On(Observations, HttpStatusCode.OK, """{ "total_results": 0 }""");
        var client = Client(Options(intervalSeconds: 1));

        await Search(client);
        await Search(client);
        await Search(client);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], _waits);
    }

    [Fact]
    public async Task Pacing_CountsTimeThatPassedAnyway()
    {
        _http.On(Observations, HttpStatusCode.OK, """{ "total_results": 0 }""");
        var client = Client(Options(intervalSeconds: 1));

        await Search(client);
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        await Search(client);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await Search(client);

        Assert.Equal([TimeSpan.FromMilliseconds(600)], _waits);
    }

    [Fact]
    public async Task Pacing_ZeroInterval_NeverWaits()
    {
        _http.On(Observations, HttpStatusCode.OK, """{ "total_results": 0 }""");
        var client = Client(Options(intervalSeconds: 0));

        await Search(client);
        await Search(client);

        Assert.Empty(_waits);
    }

    [Fact]
    public async Task Pacing_AlsoSpacesTheRetryAfterTheBackoff()
    {
        Script(Observations, Respond(HttpStatusCode.ServiceUnavailable), Respond(HttpStatusCode.OK, """{ "total_results": 1 }"""));

        await Search(Client(Options(baseSeconds: 0.25, intervalSeconds: 1)));

        // The 250 ms backoff counts toward the second's spacing; only the remaining 750 ms is added.
        Assert.Equal([TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.75)], _waits);
    }

    [Fact]
    public void Options_NoAttemptsOrNegativeTimes_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => Client(Options(maxAttempts: 0)));
        Assert.Throws<ArgumentException>(() => Client(Options(baseSeconds: -1)));
        Assert.Throws<ArgumentException>(() => Client(Options(maxSeconds: -1)));
        Assert.Throws<ArgumentException>(() => Client(Options(intervalSeconds: -1)));
    }

    [Fact]
    public void Options_Defaults_DoNotRetryButStayUnderTheDocumentedRate()
    {
        var options = new INaturalistOptions { ClientId = "c" };

        Assert.Equal(1, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), options.MinRequestInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.MaxRetryDelay);
    }

    [Fact]
    public void Exception_LongBody_IsCutInTheMessageButCompleteInResponseBody()
    {
        var page = "<html>" + new string('x', 5000) + "</html>";

        var ex = new INaturalistApiException("Taxon search", HttpStatusCode.BadGateway, page);

        Assert.Equal(page, ex.ResponseBody);
        Assert.True(ex.Message.Length < 1200);
        Assert.EndsWith("... (truncated)", ex.Message);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public void Exception_ShortBody_IsPassedThroughUnchanged()
    {
        var ex = new INaturalistApiException("Creating the observation", HttpStatusCode.UnprocessableEntity, """{"error":"Observed on can't be in the future"}""");

        Assert.Contains("""{"error":"Observed on can't be in the future"}""", ex.Message);
        Assert.Null(ex.RetryAfter);
    }

    [Fact]
    public async Task Retry_IsLoggedAsAWarningWithoutUrlOrToken()
    {
        Script(Observations, Respond(HttpStatusCode.ServiceUnavailable, "busy"), Respond(HttpStatusCode.OK));
        var logger = new CapturingLogger();

        await Search(Client(logger: logger));

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Duplicate check", entry.Message);
        Assert.Contains("503", entry.Message);
        Assert.Contains("attempt 1 of 3", entry.Message);
        // The URL carries coordinates (observation data, Debug only) and the request carries the token.
        Assert.DoesNotContain("lat=", entry.Message);
        Assert.DoesNotContain("50.11", entry.Message);
        Assert.DoesNotContain("jwt", entry.Message);
        Assert.DoesNotContain("busy", entry.Message);
    }

    private INaturalistPublisher Publisher(INaturalistOptions options) =>
        new(options, new HttpClient(_http), _ => Task.FromResult("jwt-token"), null, null, Wait);

    [Fact]
    public async Task Publish_TransientFailureOfTheTaxonSearch_StillEndsInAResult()
    {
        Script("GET /v2/taxa/autocomplete",
            Respond(HttpStatusCode.ServiceUnavailable),
            Respond(HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }"""));
        _http.On(Observations, HttpStatusCode.OK, """{ "total_results": 0, "results": [] }""");

        var result = await Publisher(Options()).PublishAsync(TestData.Candidate(), TestData.Evidence(), new PublishOptions());

        Assert.Equal(PublishStatus.WouldCreate, result.Status);
    }

    [Fact]
    public async Task Publish_ServerErrorOnTheUpload_IsNotRepeatedAndLeavesTheObservationForResume()
    {
        _http
            .On("GET /v2/taxa/autocomplete", HttpStatusCode.OK, """{ "results": [ { "id": 99, "name": "Pipistrellus pipistrellus", "rank": "species" } ] }""")
            .On(Observations, HttpStatusCode.OK, """{ "total_results": 0, "results": [] }""")
            .On(Create, HttpStatusCode.OK, """{ "results": [ { "id": 123, "uuid": "uuid-1" } ] }""")
            .On("POST /v2/observation_photos", HttpStatusCode.ServiceUnavailable, "busy");

        var result = await Publisher(Options()).PublishAsync(TestData.Candidate(), TestData.Evidence(), new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, result.Status);
        Assert.Equal(PublishStep.AttachSpectrogram, result.InterruptedStep);
        Assert.Equal("123", result.ObservationId);
        Assert.Single(_http.Requests, r => r.Method == "POST" && r.PathAndQuery.StartsWith("/v2/observation_photos", StringComparison.Ordinal));
    }

    /// <summary>A clock that only moves when told to, so pacing is deterministic. Ticks are milliseconds.</summary>
    private sealed class FakeClock(DateTimeOffset utcNow) : TimeProvider
    {
        private long _ms;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ms;

        public override DateTimeOffset GetUtcNow() => utcNow.AddMilliseconds(_ms);

        public void Advance(TimeSpan by) => _ms += (long)by.TotalMilliseconds;
    }
}
