using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatInspectorPublisher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Thin wrapper around the iNaturalist REST endpoints needed to create observations with photo and sound evidence.</summary>
internal sealed partial class INaturalistApiClient
{
    private const int MaxLoggedBodyLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly INaturalistOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly RequestPacer _pacer;

    /// <param name="options">Adapter configuration, including the retry and pacing settings.</param>
    /// <param name="http">HTTP client to send with.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="time">Clock for the pacing and the default delay; tests inject their own.</param>
    /// <param name="delay">How to wait before a retry or between paced requests; defaults to <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>.</param>
    public INaturalistApiClient(
        INaturalistOptions options,
        HttpClient http,
        ILogger? logger = null,
        TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        options.ValidateForRequests();
        _options = options;
        _http = http;
        _logger = logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
        _delay = delay ?? ((wait, ct) => Task.Delay(wait, _time, ct));
        _pacer = new RequestPacer(options.MinRequestInterval, _time, _delay);
    }

    // Debug only: URLs and bodies can hold observation data (coordinates, descriptions), never a token.
    [LoggerMessage(EventId = 3001, EventName = "HttpCall", Level = LogLevel.Debug, Message = "{Method} {Url} -> {Status}; body: {Body}")]
    private static partial void LogHttpCall(ILogger logger, HttpMethod method, Uri? url, int status, string body);

    [LoggerMessage(EventId = 3002, EventName = "NoNumericObservationId", Level = LogLevel.Warning,
        Message = "iNaturalist returned no numeric observation id; continuing with the uuid {Uuid}")]
    private static partial void LogNoNumericId(ILogger logger, string uuid);

    // Warning, without the URL: it can hold coordinates, and only Debug may log observation data. The operation names the call.
    [LoggerMessage(EventId = 3003, EventName = "RequestRetrying", Level = LogLevel.Warning,
        Message = "{Operation} failed ({Reason}); attempt {Attempt} of {MaxAttempts}, trying again in {Delay}")]
    private static partial void LogRetrying(ILogger logger, string operation, string reason, int attempt, int maxAttempts, TimeSpan delay);

    private static HttpRequestMessage NewRequest(HttpMethod method, string url, string bearerToken, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    /// <summary>
    /// Sends the request and returns the body, throwing <see cref="INaturalistApiException"/> on a
    /// non-success status. Every call is logged at debug level with a truncated body, since
    /// undocumented response shapes have repeatedly broken assumptions. Bodies containing tokens
    /// are never logged (<paramref name="logBody"/>). Requests are paced (<see cref="INaturalistOptions.MinRequestInterval"/>),
    /// and a failure that is safe to repeat is retried (<see cref="INaturalistOptions.MaxAttempts"/>); the request is
    /// built anew for every attempt because its content cannot be sent twice. After the last attempt the failure is thrown unchanged.
    /// </summary>
    private async Task<string> SendAsync(Func<HttpRequestMessage> createRequest, string operation, CancellationToken ct, bool logBody = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            await _pacer.WaitAsync(ct);

            using var request = createRequest();
            var isRead = request.Method == HttpMethod.Get;
            Exception failure;
            TimeSpan? retryAfter = null;
            try
            {
                using var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var shown = !logBody ? "(not logged)" : body.Length > MaxLoggedBodyLength ? body[..MaxLoggedBodyLength] + "... (truncated)" : body;
                    LogHttpCall(_logger, request.Method, request.RequestUri, (int)response.StatusCode, shown);
                }

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                retryAfter = ReadRetryAfter(response);
                failure = new INaturalistApiException(operation, response.StatusCode, body, retryAfter);
            }
            catch (HttpRequestException ex)
            {
                failure = ex;
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // The HttpClient timeout, not the caller's cancellation.
                failure = ex;
            }

            var wait = attempt < _options.MaxAttempts && IsSafeToRepeat(failure, isRead) ? NextDelay(attempt, retryAfter) : null;
            if (wait is null)
            {
                Rethrow(failure);
            }

            LogRetrying(_logger, operation, Describe(failure), attempt, _options.MaxAttempts, wait.Value);
            await _delay(wait.Value, ct);
        }
    }

    /// <summary>
    /// 429 is safe for every request: iNaturalist refused it before processing. A read request is also repeated after a
    /// server error, a network error or a timeout. A write request is not: the server may have processed it, and a repeat
    /// could create a second observation or upload. The exception is a connection that could not be set up, where nothing was sent.
    /// </summary>
    private static bool IsSafeToRepeat(Exception failure, bool isRead) => failure switch
    {
        INaturalistApiException { StatusCode: HttpStatusCode.TooManyRequests } => true,
        INaturalistApiException e => isRead && e.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError } => true,
        HttpRequestException or OperationCanceledException => isRead,
        _ => false,
    };

    /// <summary>The wait before the next attempt, or null when the server asked for longer than <see cref="INaturalistOptions.MaxRetryDelay"/>.</summary>
    private TimeSpan? NextDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } asked)
        {
            return asked <= _options.MaxRetryDelay ? asked : null;
        }

        var backoff = _options.RetryBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(backoff, _options.MaxRetryDelay.TotalSeconds));
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date - _time.GetUtcNow());
        return wait is { } w ? (w < TimeSpan.Zero ? TimeSpan.Zero : w) : null;
    }

    private static string Describe(Exception failure) => failure switch
    {
        INaturalistApiException e => $"{(int)e.StatusCode} {e.StatusCode}",
        OperationCanceledException => "timeout",
        _ => failure.GetType().Name,
    };

    [DoesNotReturn]
    private static void Rethrow(Exception failure) => ExceptionDispatchInfo.Capture(failure).Throw();

    /// <summary>Exchanges a raw OAuth access token for the JWT the REST API accepts.</summary>
    public async Task<string> ExchangeOAuthTokenForJwtAsync(string oauthAccessToken, CancellationToken ct)
    {
        var body = await SendAsync(() => NewRequest(HttpMethod.Get, _options.ApiTokenExchangeUrl, oauthAccessToken), "OAuth token to JWT exchange", ct, logBody: false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("api_token", out var token) && token.GetString() is { Length: > 0 } jwt
            ? jwt
            : throw new InvalidOperationException("The token exchange response contains no api_token.");
    }

    public async Task<string> GetAuthenticatedUsernameAsync(string jwt, CancellationToken ct)
    {
        var body = await SendAsync(() => NewRequest(HttpMethod.Get, $"{_options.ApiBaseUrlV1}/users/me", jwt), "Fetching the user profile", ct);
        using var doc = JsonDocument.Parse(body);
        var results = doc.RootElement.GetProperty("results");
        return results.GetArrayLength() > 0 ? results[0].GetProperty("login").GetString() ?? "?" : "?";
    }

    /// <summary>
    /// Resolves a scientific name to a taxon of the expected rank. Only an exact (case-insensitive)
    /// name match of an active taxon is accepted: silently falling back to the first autocomplete hit
    /// would file the observation under a wrong species. A rank that is missing from the response
    /// counts as a mismatch, and so does more than one match: nothing is guessed.
    /// </summary>
    public async Task<TaxonLookup> ResolveTaxonAsync(string scientificName, string expectedRank, string jwt, CancellationToken ct)
    {
        var url = $"{_options.ApiBaseUrlV2}/taxa/autocomplete?q={Uri.EscapeDataString(scientificName)}&fields=id,name,rank,is_active,matched_term";
        var body = await SendAsync(() => NewRequest(HttpMethod.Get, url, jwt), "Taxon search", ct);
        var results = JsonSerializer.Deserialize<TaxaAutocompleteResponse>(body)?.Results ?? [];

        var exact = results.Where(t => SameName(t.Name, scientificName) && t.IsActive != false).ToList();
        var ofRank = exact.Where(t => string.Equals(t.Rank, expectedRank, StringComparison.OrdinalIgnoreCase)).DistinctBy(t => t.Id).ToList();
        if (ofRank.Count == 1)
        {
            return new TaxonLookup(ofRank[0], null);
        }

        if (ofRank.Count > 1)
        {
            return new TaxonLookup(null, $"'{scientificName}' is ambiguous on iNaturalist (taxon ids {string.Join(", ", ofRank.Select(t => t.Id))}).");
        }

        if (exact.Count > 0)
        {
            var found = string.Join(", ", exact.Select(t => t.Rank ?? "unknown rank"));
            return new TaxonLookup(null, $"'{scientificName}' exists on iNaturalist only with rank {found}, expected {expectedRank}.");
        }

        var synonym = results.FirstOrDefault(t => SameName(t.MatchedTerm, scientificName));
        return new TaxonLookup(null, synonym is null
            ? $"Taxon '{scientificName}' not found on iNaturalist."
            : $"'{scientificName}' is not the current name on iNaturalist, which lists it as '{synonym.Name}'. Use that name in the input file.");
    }

    private static bool SameName(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The authenticated user's observations of this taxon on this date near these coordinates (total count plus the first page of details).</summary>
    public async Task<ObservationsSearchResponse> FindExistingObservationsAsync(int taxonId, DateOnly date, double lat, double lon, string jwt, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = $"{_options.ApiBaseUrlV1}/observations?taxon_id={taxonId}" +
                  $"&d1={date.ToString("yyyy-MM-dd", inv)}&d2={date.ToString("yyyy-MM-dd", inv)}" +
                  $"&lat={lat.ToString(inv)}&lng={lon.ToString(inv)}&radius={_options.DuplicateCheckRadiusKm.ToString(inv)}&mine_only=true";
        var body = await SendAsync(() => NewRequest(HttpMethod.Get, url, jwt), "Duplicate check", ct);
        return JsonSerializer.Deserialize<ObservationsSearchResponse>(body) ?? new ObservationsSearchResponse();
    }

    public async Task<CreateObservationResult> CreateObservationAsync(ObservationPayload payload, string jwt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new CreateObservationRequest { Observation = payload }, SerializerOptions);
        var body = await SendAsync(
            () => NewRequest(HttpMethod.Post, $"{_options.ApiBaseUrlV2}/observations", jwt, new StringContent(json, Encoding.UTF8, "application/json")),
            "Creating the observation", ct);

        var observation = JsonSerializer.Deserialize<CreateObservationResponse>(body)?.Results.FirstOrDefault()
            ?? throw new InvalidOperationException($"Empty response when creating the observation: {body}");
        if (string.IsNullOrEmpty(observation.Uuid))
        {
            throw new InvalidOperationException($"The response has no observation uuid: {body}");
        }

        if (observation.Id == 0)
        {
            // Seen in practice: a valid uuid but no numeric id. Only the uuid is needed to attach media.
            LogNoNumericId(_logger, observation.Uuid);
        }

        return observation;
    }

    /// <summary>Uploads a photo and links it to the observation in one multipart request.</summary>
    public Task AttachPhotoAsync(string observationUuid, EvidenceFile file, string jwt, CancellationToken ct) =>
        UploadAsync($"{_options.ApiBaseUrlV2}/observation_photos", "observation_photo[observation_id]", observationUuid, file, "Attaching the spectrogram", jwt, ct);

    /// <summary>Uploads a sound and links it to the observation in one multipart request.</summary>
    public Task AttachSoundAsync(string observationUuid, EvidenceFile file, string jwt, CancellationToken ct) =>
        UploadAsync($"{_options.ApiBaseUrlV2}/observation_sounds", "observation_sound[observation_id]", observationUuid, file, "Attaching the audio", jwt, ct);

    private async Task UploadAsync(string url, string idField, string observationUuid, EvidenceFile file, string operation, string jwt, CancellationToken ct)
    {
        // The two-step flow (upload, then link by id) was rejected by the real API with "No photo
        // specified"; the combined multipart upload+link from the v2 OpenAPI spec works.
        await SendAsync(() =>
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(observationUuid), idField);
            content.Add(new ByteArrayContent(file.Content), "file", file.FileName);
            return NewRequest(HttpMethod.Post, url, jwt, content);
        }, operation, ct);
    }
}
