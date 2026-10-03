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
/// writes nothing. An observation that an earlier run left without its full evidence is completed
/// instead of being reported as a duplicate.
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
        var step = PublishStep.Preparation;
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

            var payload = BuildPayload(candidate, taxon.Id);
            var existing = await _api.FindExistingObservationsAsync(
                taxon.Id, DateOnly.FromDateTime(candidate.ObservedAt.DateTime), candidate.Latitude, candidate.Longitude, jwt, ct);

            string uuid;
            var resumed = false;
            if (existing.TotalResults > 0)
            {
                var match = existing.Results.FirstOrDefault(o => IsIncompleteOwnObservation(o, payload.Description));
                if (match is null)
                {
                    var known = existing.Results.FirstOrDefault(o => o.Id != 0 || o.Uuid.Length > 0);
                    if (known is not null)
                    {
                        (observationId, url) = Identify(known.Id, known.Uuid);
                        photoAttached = known.Photos is { Count: > 0 };
                        soundAttached = known.Sounds is { Count: > 0 };
                    }

                    return Result(PublishStatus.SkippedDuplicate, "An equivalent observation already exists on iNaturalist.");
                }

                // An earlier run created this observation and stopped before its evidence was complete.
                // Only an observation whose description is exactly what this entry produces is completed;
                // anything else of the user's stays untouched.
                (observationId, url) = Identify(match.Id, match.Uuid);
                uuid = match.Uuid;
                photoAttached = match.Photos is { Count: > 0 };
                soundAttached = match.Sounds is { Count: > 0 };
                resumed = true;
                _logger.LogInformation("Completing the incomplete iNaturalist observation {ObservationId} of {Species}", observationId, candidate.ScientificName);
            }
            else
            {
                step = PublishStep.CreateObservation;
                var created = await _api.CreateObservationAsync(payload, jwt, ct);
                uuid = created.Uuid;
                (observationId, url) = Identify(created.Id, created.Uuid);
                // Logged at once: if anything after this fails, this line is the trail to the observation.
                _logger.LogInformation("Created iNaturalist observation {ObservationId} of {Species}", observationId, candidate.ScientificName);
            }

            if (!photoAttached)
            {
                step = PublishStep.AttachSpectrogram;
                await _api.AttachPhotoAsync(uuid, evidence.Spectrogram, jwt, ct);
                photoAttached = true;
            }

            if (!soundAttached)
            {
                step = PublishStep.AttachAudio;
                await _api.AttachSoundAsync(uuid, evidence.Audio, jwt, ct);
                soundAttached = true;
            }

            return resumed
                ? Result(PublishStatus.Resumed, $"Completed the incomplete observation {observationId}.")
                : Result(PublishStatus.Created, $"Created observation {observationId}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result(PublishStatus.Cancelled, Interrupted("Cancelled"), interruptedStep: step);
        }
        catch (Exception ex)
        {
            // Includes an OperationCanceledException that is not the caller's, such as an HttpClient timeout.
            return Result(PublishStatus.Failed, ex.Message + Partial(), ex, step);
        }

        string Interrupted(string what) => observationId is not null
            ? $"{what} during {step}.{Partial()}"
            : step == PublishStep.CreateObservation
                ? $"{what} while the observation was being created; it may or may not exist on iNaturalist."
                : $"{what} during {step}; nothing was created.";

        string Partial() => observationId is null
            ? ""
            : $" The observation {observationId} was already created; its evidence is incomplete.";

        PublishResult Result(PublishStatus status, string message, Exception? error = null, PublishStep? interruptedStep = null) => new()
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
            InterruptedStep = interruptedStep,
        };
    }

    private static (string? Id, string? Url) Identify(long id, string uuid) => id != 0
        ? (id.ToString(CultureInfo.InvariantCulture), $"https://www.inaturalist.org/observations/{id}")
        : (uuid.Length > 0 ? uuid : null, null);

    /// <summary>
    /// True for an observation this package evidently created for the same entry (identical description,
    /// which includes the exact time, measurements and comment) that still lacks its spectrogram or audio.
    /// When photos or sounds are not reported, the state is unknown and nothing is touched.
    /// </summary>
    private static bool IsIncompleteOwnObservation(ExistingObservation observation, string? description) =>
        observation.Uuid.Length > 0
        && observation.Photos is not null
        && observation.Sounds is not null
        && (observation.Photos.Count == 0 || observation.Sounds.Count == 0)
        && NormalizeText(observation.Description) == NormalizeText(description);

    private static string NormalizeText(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
