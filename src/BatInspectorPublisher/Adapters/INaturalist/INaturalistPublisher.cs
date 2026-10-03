using System.Collections.Concurrent;
using System.Globalization;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Publishes candidates to iNaturalist: resolve taxon, duplicate check, create the observation,
/// attach spectrogram (photo) and audio (sound). A dry run (the default) resolves the taxon but
/// writes nothing.
/// </summary>
public sealed class INaturalistPublisher : IObservationPublisher
{
    private readonly INaturalistOptions _options;
    private readonly INaturalistApiClient _api;
    private readonly Func<CancellationToken, Task<string>> _getAccessToken;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Taxon> _taxonCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string PlatformId => "inaturalist";

    /// <summary>Creates a publisher that obtains its API token from <paramref name="authenticator"/>.</summary>
    public INaturalistPublisher(
        INaturalistOptions options,
        HttpClient httpClient,
        INaturalistAuthenticator authenticator,
        ILogger<INaturalistPublisher>? logger = null)
        : this(options, httpClient, authenticator.EnsureAuthenticatedAsync, logger)
    {
    }

    internal INaturalistPublisher(
        INaturalistOptions options,
        HttpClient httpClient,
        Func<CancellationToken, Task<string>> getAccessToken,
        ILogger? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger.Instance;
        _api = new INaturalistApiClient(options, httpClient, _logger);
        _getAccessToken = getAccessToken;
    }

    /// <inheritdoc />
    public async Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default)
    {
        string? observationId = null;
        string? url = null;
        var photoAttached = false;
        var soundAttached = false;

        try
        {
            var jwt = await _getAccessToken(ct);

            var taxon = await ResolveTaxonAsync(candidate.ScientificName, jwt, ct);
            if (taxon is null)
            {
                return Result(PublishStatus.SkippedUnresolvedTaxon, $"Taxon '{candidate.ScientificName}' not found on iNaturalist.");
            }

            if (!options.Commit)
            {
                return Result(PublishStatus.WouldCreate, $"Dry run: would create an observation of {candidate.ScientificName} (taxon_id={taxon.Id}).");
            }

            if (await _api.HasExistingObservationAsync(
                    taxon.Id, DateOnly.FromDateTime(candidate.ObservedAt.DateTime), candidate.Latitude, candidate.Longitude, jwt, ct))
            {
                return Result(PublishStatus.SkippedDuplicate, "An equivalent observation already exists on iNaturalist.");
            }

            var created = await _api.CreateObservationAsync(BuildPayload(candidate, taxon.Id), jwt, ct);
            observationId = created.Id != 0 ? created.Id.ToString(CultureInfo.InvariantCulture) : created.Uuid;
            url = created.Id != 0 ? $"https://www.inaturalist.org/observations/{created.Id}" : null;

            await _api.AttachPhotoAsync(created.Uuid, evidence.Spectrogram, jwt, ct);
            photoAttached = true;
            await _api.AttachSoundAsync(created.Uuid, evidence.Audio, jwt, ct);
            soundAttached = true;

            return Result(PublishStatus.Created, $"Created observation {observationId}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var partial = observationId is null ? "" : $" The observation {observationId} was already created; its evidence is incomplete.";
            return Result(PublishStatus.Failed, ex.Message + partial, ex);
        }

        PublishResult Result(PublishStatus status, string message, Exception? error = null) => new()
        {
            Candidate = candidate,
            PlatformId = PlatformId,
            Status = status,
            ObservationId = observationId,
            Url = url,
            SpectrogramAttached = photoAttached,
            AudioAttached = soundAttached,
            Message = message,
            Error = error,
        };
    }

    private async Task<Taxon?> ResolveTaxonAsync(string scientificName, string jwt, CancellationToken ct)
    {
        if (_taxonCache.TryGetValue(scientificName, out var cached))
        {
            return cached;
        }

        var taxon = await _api.ResolveTaxonAsync(scientificName, jwt, ct);
        if (taxon is not null)
        {
            _taxonCache[scientificName] = taxon;
        }

        return taxon;
    }

    internal ObservationPayload BuildPayload(ObservationCandidate candidate, int taxonId) => new()
    {
        TaxonId = taxonId,
        ObservedOnString = candidate.ObservedAt.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Latitude = candidate.Latitude,
        Longitude = candidate.Longitude,
        // Always a string: v2 rejects null. The input format has no place name, so use the coordinates.
        PlaceGuess = string.Create(CultureInfo.InvariantCulture, $"{candidate.Latitude:F5}, {candidate.Longitude:F5}"),
        SpeciesGuess = candidate.ScientificName,
        Description = DescriptionBuilder.Build(candidate, _options.DescriptionPrefix),
        TagList = _options.TagList,
    };
}
