using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatInspectorPublisher.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Thin wrapper around the iNaturalist REST endpoints needed to create observations with photo and sound evidence.</summary>
internal sealed class INaturalistApiClient
{
    private const int MaxLoggedBodyLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly INaturalistOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public INaturalistApiClient(INaturalistOptions options, HttpClient http, ILogger? logger = null)
    {
        _options = options;
        _http = http;
        _logger = logger ?? NullLogger.Instance;
    }

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
    /// are never logged (<paramref name="logBody"/>).
    /// </summary>
    private async Task<string> SendAsync(HttpRequestMessage request, string operation, CancellationToken ct, bool logBody = true)
    {
        using (request)
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var shown = !logBody ? "(not logged)" : body.Length > MaxLoggedBodyLength ? body[..MaxLoggedBodyLength] + "... (truncated)" : body;
                _logger.LogDebug("{Method} {Url} -> {Status}; body: {Body}", request.Method, request.RequestUri, (int)response.StatusCode, shown);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new INaturalistApiException(operation, response.StatusCode, body);
            }

            return body;
        }
    }

    /// <summary>Exchanges a raw OAuth access token for the JWT the REST API accepts.</summary>
    public async Task<string> ExchangeOAuthTokenForJwtAsync(string oauthAccessToken, CancellationToken ct)
    {
        var body = await SendAsync(NewRequest(HttpMethod.Get, _options.ApiTokenExchangeUrl, oauthAccessToken), "OAuth token to JWT exchange", ct, logBody: false);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("api_token", out var token) && token.GetString() is { Length: > 0 } jwt
            ? jwt
            : throw new InvalidOperationException("The token exchange response contains no api_token.");
    }

    public async Task<string> GetAuthenticatedUsernameAsync(string jwt, CancellationToken ct)
    {
        var body = await SendAsync(NewRequest(HttpMethod.Get, $"{_options.ApiBaseUrlV1}/users/me", jwt), "Fetching the user profile", ct);
        using var doc = JsonDocument.Parse(body);
        var results = doc.RootElement.GetProperty("results");
        return results.GetArrayLength() > 0 ? results[0].GetProperty("login").GetString() ?? "?" : "?";
    }

    /// <summary>
    /// Resolves a scientific name to a taxon. Only an exact (case-insensitive) name match is
    /// accepted: silently falling back to the first autocomplete hit would file the observation
    /// under a wrong species.
    /// </summary>
    public async Task<Taxon?> ResolveTaxonAsync(string scientificName, string jwt, CancellationToken ct)
    {
        var url = $"{_options.ApiBaseUrlV2}/taxa/autocomplete?q={Uri.EscapeDataString(scientificName)}&fields=id,name,rank";
        var body = await SendAsync(NewRequest(HttpMethod.Get, url, jwt), "Taxon search", ct);
        var result = JsonSerializer.Deserialize<TaxaAutocompleteResponse>(body);
        return result?.Results.FirstOrDefault(t => string.Equals(t.Name, scientificName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The authenticated user's observations of this taxon on this date near these coordinates (total count plus the first page of details).</summary>
    public async Task<ObservationsSearchResponse> FindExistingObservationsAsync(int taxonId, DateOnly date, double lat, double lon, string jwt, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = $"{_options.ApiBaseUrlV1}/observations?taxon_id={taxonId}" +
                  $"&d1={date.ToString("yyyy-MM-dd", inv)}&d2={date.ToString("yyyy-MM-dd", inv)}" +
                  $"&lat={lat.ToString(inv)}&lng={lon.ToString(inv)}&radius={_options.DuplicateCheckRadiusKm.ToString(inv)}&mine_only=true";
        var body = await SendAsync(NewRequest(HttpMethod.Get, url, jwt), "Duplicate check", ct);
        return JsonSerializer.Deserialize<ObservationsSearchResponse>(body) ?? new ObservationsSearchResponse();
    }

    public async Task<CreateObservationResult> CreateObservationAsync(ObservationPayload payload, string jwt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new CreateObservationRequest { Observation = payload }, SerializerOptions);
        var request = NewRequest(HttpMethod.Post, $"{_options.ApiBaseUrlV2}/observations", jwt,
            new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await SendAsync(request, "Creating the observation", ct);

        var observation = JsonSerializer.Deserialize<CreateObservationResponse>(body)?.Results.FirstOrDefault()
            ?? throw new InvalidOperationException($"Empty response when creating the observation: {body}");
        if (string.IsNullOrEmpty(observation.Uuid))
        {
            throw new InvalidOperationException($"The response has no observation uuid: {body}");
        }

        if (observation.Id == 0)
        {
            // Seen in practice: a valid uuid but no numeric id. Only the uuid is needed to attach media.
            _logger.LogWarning("iNaturalist returned no numeric observation id; continuing with the uuid {Uuid}", observation.Uuid);
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
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(observationUuid), idField);
        content.Add(new ByteArrayContent(file.Content), "file", file.FileName);
        await SendAsync(NewRequest(HttpMethod.Post, url, jwt, content), operation, ct);
    }
}
