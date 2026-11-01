using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>The platform's answer to a species lookup: its id for the entry, or the reason it has none.</summary>
/// <param name="Id">The platform's id of the taxon entry; null when it has none.</param>
/// <param name="Problem">The platform's own message when there is no id, passed through unchanged.</param>
internal readonly record struct SpeciesLookup(long? Id, string? Problem);

/// <summary>An observation of the user that the platform already holds for an entry.</summary>
/// <param name="EntryId">The identifier this package gave it (see <see cref="NaturguckerEntryId"/>).</param>
/// <param name="HeadId">The platform's id of the observation, the one a photo is attached to.</param>
/// <param name="HasPhoto">True when the platform holds at least one photo of it.</param>
internal readonly record struct OwnObservation(string EntryId, long HeadId, bool HasPhoto);

/// <summary>What is written for one entry: everything the platform needs to create the observation.</summary>
internal sealed record NewObservation(ObservationCandidate Candidate, long SpeciesId, bool Uncertain, string Description, string EntryId);

/// <summary>
/// Thin client of the platform's one endpoint. It sends, reads and reports; every rule (what is a bat, what is published)
/// lives outside. The login travels with the request, so nothing about a request is ever logged or put in an exception.
/// <para>
/// Draft where the platform's behavior is not confirmed (marked at the place): the transport of each call (GET or POST), the
/// encoding of the photo upload and some field names. The live test settles them; a stub test of this class proves only
/// that it does what the draft says, not that the platform agrees.
/// </para>
/// </summary>
internal sealed class NaturguckerApiClient
{
    /// <summary>The platform's endpoint. Not an option: hosts get no way to point the login somewhere else.</summary>
    internal static readonly Uri DefaultBaseAddress = new("https://www.naturgucker.de/mobil/");

    /// <summary>Gap between two requests: the platform names no limit, so the adapter is slow rather than sorry.</summary>
    internal static readonly TimeSpan DefaultMinRequestInterval = TimeSpan.FromSeconds(1);

    private const int PageSize = 100;
    private const int MaxPages = 20;

    private readonly HttpClient _http;
    private readonly NaturguckerOptions _options;
    private readonly Uri _baseAddress;
    private readonly TimeSpan _minRequestInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _pacing = new(1, 1);
    private long _lastRequest;

    /// <param name="http">The host's client; its timeout and proxy apply.</param>
    /// <param name="options">The host's options, already validated.</param>
    /// <param name="baseAddress">Test-only: another https address than the platform's.</param>
    /// <param name="minRequestInterval">Test-only: another gap between requests; never below the default for a host.</param>
    /// <param name="delay">Test-only: replaces the real wait.</param>
    public NaturguckerApiClient(
        HttpClient http,
        NaturguckerOptions options,
        Uri? baseAddress = null,
        TimeSpan? minRequestInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _baseAddress = baseAddress ?? DefaultBaseAddress;
        if (_baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The platform is only ever called over https: the login travels with every request.", nameof(baseAddress));
        }

        _http = http;
        _options = options;
        _minRequestInterval = minRequestInterval ?? DefaultMinRequestInterval;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Looks up the platform's id for <paramref name="taxon"/>. Draft: genus and epithet go in lower case (the platform ignores
    /// the case, checked live); GET and POST both work, checked live.
    /// </summary>
    /// <exception cref="NaturguckerApiException">An HTTP error, or an answer that is not understood.</exception>
    public async Task<SpeciesLookup> FindSpeciesAsync(NaturguckerTaxon taxon, CancellationToken ct)
    {
        var answer = await SendAsync(() => FormRequest(
        [
            ("modul", "naturguckerartid"),
            ("gattung", taxon.Genus.ToLowerInvariant()),
            ("taxon", taxon.Epithet.ToLowerInvariant()),
            ("service", Service),
        ]), ct).ConfigureAwait(false);

        var id = answer.TryGetProperty("naturguckerartid", out var idElement) && idElement.TryGetInt64(out var value) ? value : 0;
        return Status(answer) == 0 && id != 0
            ? new(id, null)
            : new(null, Message(answer) ?? "The platform has no entry for this taxon.");
    }

    /// <summary>
    /// Asks the platform whether it knows the account (its own login call). Needed because the query for the user's own
    /// observations answers an unknown login like an empty result (live 2026-11-01, provisional): without this check a refused
    /// login would look like "nothing published yet" and the duplicate check would be blind. Only the status and the message
    /// are read: the platform's answer to a known account carries the whole profile, which is never kept, logged or passed on.
    /// </summary>
    /// <exception cref="NaturguckerApiException">An HTTP error, an answer that is not understood, or a login the platform refuses.</exception>
    public async Task CheckLoginAsync(CancellationToken ct)
    {
        var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Query(
        [
            ("modul", "login"),
            ("service", Service),
            ("email1", _options.Email),
            ("md5", _options.PasswordHash()),
        ])), ct).ConfigureAwait(false);

        if (Status(answer) != 0)
        {
            throw new NaturguckerApiException($"naturgucker refused the login: {Message(answer) ?? "no reason given"}");
        }
    }

    /// <summary>
    /// Finds the user's observation of <paramref name="day"/> that carries <paramref name="entryId"/>, or null. Draft: a GET
    /// with the login in the address; the address is never logged. Only observations this app's
    /// service number created are asked for.
    /// </summary>
    /// <exception cref="NaturguckerApiException">An HTTP error, a refused query, or an answer that is not understood. A refused login is not
    /// reported here (see <see cref="CheckLoginAsync"/>): the caller checks the login first.</exception>
    public async Task<OwnObservation?> FindOwnObservationAsync(DateOnly day, string entryId, CancellationToken ct)
    {
        var date = Date(day);
        var offset = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var currentOffset = offset;
            var answer = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, Query(
            [
                ("modul", "beobachtungen"),
                ("email1", _options.Email),
                ("md5", _options.PasswordHash()),
                ("service", Service),
                ("nurservicenr", Service),
                ("datumvon", date),
                ("datumbis", date),
                ("offset", currentOffset.ToString(CultureInfo.InvariantCulture)),
                ("zeilen", PageSize.ToString(CultureInfo.InvariantCulture)),
            ])), ct).ConfigureAwait(false);

            if (Status(answer) is not >= 0)
            {
                throw new NaturguckerApiException($"naturgucker refused the query for own observations: {Message(answer) ?? "no reason given"}");
            }

            var count = 0;
            if (answer.TryGetProperty("beobachtungen", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    count++;
                    if (NaturguckerEntryId.Matches(Text(item, "guid"), entryId) && Number(item, "kopfid") is { } head)
                    {
                        return new(entryId, head, !string.IsNullOrWhiteSpace(Text(item, "belegbild")));
                    }
                }
            }

            var more = answer.TryGetProperty("more", out var moreElement) && moreElement.ValueKind == JsonValueKind.True;
            var next = answer.TryGetProperty("offset", out var offsetElement) && offsetElement.TryGetInt32(out var nextOffset) ? nextOffset : offset + count;
            if (!more)
            {
                return null;
            }

            // "More" without progress would end the search early and report a miss: a duplicate could then be created.
            if (count == 0 || next <= offset)
            {
                throw new NaturguckerApiException("naturgucker reports more own observations but does not advance; the duplicate check would be incomplete.");
            }

            offset = next;
        }

        throw new NaturguckerApiException("naturgucker lists more own observations for one day than this adapter reads; the duplicate check would be incomplete.");
    }

    /// <summary>
    /// Creates the observation and returns the platform's id of it. Draft: one JSON body in the platform's batch form with a
    /// single observation and no photo (the photos follow, see <see cref="UploadPhotoAsync"/>). This form has no field for the
    /// time of day and none for the uncertainty (to be confirmed); the latter is sent anyway.
    /// </summary>
    /// <exception cref="NaturguckerApiException">An HTTP error, an answer that is not understood, or a refusal by the platform.</exception>
    public async Task<long> CreateObservationAsync(NewObservation observation, CancellationToken ct)
    {
        var candidate = observation.Candidate;
        var payload = new Dictionary<string, object?>
        {
            ["modul"] = "beobachtungsblock_eintragen2",
            ["service"] = Service,
            ["datum"] = Date(DateOnly.FromDateTime(candidate.ObservedAt.DateTime)),
            ["lat"] = candidate.Latitude,
            ["lng"] = candidate.Longitude,
            ["orttyp"] = 1,
            ["quelle"] = 2,
            ["beschreibung"] = "",
            ["email1"] = _options.Email,
            ["md5"] = _options.PasswordHash(),
            ["anmelden"] = 0,
            ["bemerkung"] = "",
            ["beobachtungen"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["fiart"] = observation.SpeciesId,
                    ["geschaetzt"] = "",
                    ["anzahl"] = 1,
                    ["fiwas"] = -1,
                    ["figeschlecht"] = -1,
                    ["fialter"] = -1,
                    ["bemerkung"] = EncodeRemark(observation.Description),
                    ["sperrung"] = _options.LockPlaceAndObserver ? 1 : 0,
                    ["bestimmungunsicher"] = observation.Uncertain ? 1 : 0,
                    ["guid"] = observation.EntryId,
                },
            },
        };

        var answer = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, _baseAddress) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") },
            ct).ConfigureAwait(false);

        return Status(answer) == 0 && Number(answer, "fibeobachtungenkopf") is { } head && head != 0
            ? head
            : throw new NaturguckerApiException($"naturgucker did not create the observation: {Message(answer) ?? "no reason given"}");
    }

    /// <summary>
    /// Attaches the photo to the observation. Draft: a multipart form with the fields of the observation and the
    /// file in one part; the encoding and the name of the file field are for the live test.
    /// </summary>
    /// <exception cref="NaturguckerApiException">An HTTP error, an answer that is not understood, or a refusal by the platform.</exception>
    public async Task UploadPhotoAsync(NewObservation observation, long headId, EvidenceFile photo, CancellationToken ct)
    {
        var candidate = observation.Candidate;
        var answer = await SendAsync(() =>
        {
            var form = new MultipartFormDataContent();
            foreach (var (name, value) in new (string, string)[]
            {
                ("modul", "bilder_uebertragen"),
                ("email", _options.Email),
                ("email1", _options.Email),
                ("md5", _options.PasswordHash()),
                ("service", Service),
                ("datum", Date(DateOnly.FromDateTime(candidate.ObservedAt.DateTime))),
                ("lat", candidate.Latitude.ToString("R", CultureInfo.InvariantCulture)),
                ("lng", candidate.Longitude.ToString("R", CultureInfo.InvariantCulture)),
                ("fiart", observation.SpeciesId.ToString(CultureInfo.InvariantCulture)),
                ("fibeobachtungenkopf", headId.ToString(CultureInfo.InvariantCulture)),
            })
            {
                form.Add(new StringContent(value), name);
            }

            var file = new ByteArrayContent(photo.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(ImageType(photo.Content));
            form.Add(file, "file", photo.FileName);
            return new HttpRequestMessage(HttpMethod.Post, _baseAddress) { Content = form };
        }, ct).ConfigureAwait(false);

        if (Status(answer) != 0)
        {
            throw new NaturguckerApiException($"naturgucker did not take the photo: {Message(answer) ?? "no reason given"}");
        }
    }

    private string Service => _options.ServiceNumber.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly day) => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Draft: the platform URL-decodes the remark even in the JSON body (a bare "%" was refused with "invalid URL-encoded character",
    /// live 2026-11-01), so everything but the unreserved characters goes out percent-encoded as UTF-8 ("+" would otherwise become
    /// a space, umlauts could be misread). Whether the text then arrives unchanged is for the live test to confirm.
    /// </summary>
    private static string EncodeRemark(string text) => Uri.EscapeDataString(text);

    private static string ImageType(byte[] content) =>
        content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF ? "image/jpeg" : "image/png";

    private HttpRequestMessage FormRequest(IEnumerable<(string Name, string Value)> fields) =>
        new(HttpMethod.Post, _baseAddress) { Content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))) };

    private Uri Query(IEnumerable<(string Name, string Value)> fields) =>
        new($"{_baseAddress.GetLeftPart(UriPartial.Path)}?{string.Join('&', fields.Select(f => $"{Uri.EscapeDataString(f.Name)}={Uri.EscapeDataString(f.Value)}"))}");

    /// <summary>Waits until the gap since the previous request has passed.</summary>
    private async Task PaceAsync(CancellationToken ct)
    {
        await _pacing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_lastRequest != 0)
            {
                var wait = _minRequestInterval - Stopwatch.GetElapsedTime(_lastRequest);
                if (wait > TimeSpan.Zero)
                {
                    await _delay(wait, ct).ConfigureAwait(false);
                }
            }

            _lastRequest = Stopwatch.GetTimestamp();
        }
        finally
        {
            _pacing.Release();
        }
    }

    private async Task<JsonElement> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        await PaceAsync(ct).ConfigureAwait(false);
        using var request = createRequest();
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // The message of the inner exception is left out of ours: it can name the address, and the address carries the login.
            throw new NaturguckerApiException("The request to naturgucker failed before an answer came back.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new NaturguckerApiException($"naturgucker answered HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(UnwrapJsonp(body));
                return document.RootElement.ValueKind == JsonValueKind.Object
                    ? document.RootElement.Clone()
                    : throw new NaturguckerApiException("naturgucker answered with JSON that is not an object.");
            }
            catch (JsonException ex)
            {
                throw new NaturguckerApiException("naturgucker answered with something that is not JSON.", ex);
            }
        }
    }

    /// <summary>The platform can answer in a callback wrapper; an answer wrapped in one ("name({...});") is read like a plain one.</summary>
    private static string UnwrapJsonp(string body)
    {
        var text = body.Trim();
        var open = text.IndexOf('(', StringComparison.Ordinal);
        if (open > 0 && text[..open].All(c => char.IsLetterOrDigit(c) || c is '_' or '$' or '.') && text.TrimEnd(';').EndsWith(')'))
        {
            return text[(open + 1)..text.TrimEnd(';').LastIndexOf(')')];
        }

        return text;
    }

    private static int? Status(JsonElement answer) =>
        answer.TryGetProperty("status", out var status) && status.TryGetInt32(out var value) ? value : null;

    private static string? Message(JsonElement answer) => Text(answer, "meldung") is { Length: > 0 } text ? text : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value) ? value : null;
}
