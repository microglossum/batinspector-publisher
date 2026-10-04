using System.Globalization;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Publishes candidates to iNaturalist: resolve taxon, duplicate check, create the observation,
/// attach spectrogram (photo) and audio (sound). Before anything else, the platform
/// validation refuses what iNaturalist would reject. A dry run (the default) goes through the same steps, including
/// the login, the taxon search and the duplicate check, and stops where the first write would happen: it reports
/// what a commit run would do and writes nothing. An observation that an earlier run left without its full evidence is completed
/// instead of being reported as a duplicate.
/// </summary>
public sealed class INaturalistPublisher : IObservationPublisher
{
    private readonly INaturalistOptions _options;
    private readonly INaturalistApiClient _api;
    private readonly Func<CancellationToken, Task<string>> _getAccessToken;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// BatInspector group and uncertain-call values that are not taxon names, and the broader taxon each is filed under.
    /// The original value stays in <c>species_guess</c> and is named in the description. Anything else that does not resolve is skipped, never
    /// filed under a guess: a typo filed under Chiroptera would be public and a corrected re-run would create a second observation.
    /// "todo" (not yet reviewed in BatInspector) is deliberately not listed.
    /// </summary>
    private static readonly Dictionary<string, (string Name, string Rank)> BroaderTaxa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nyctaloid"] = ("Chiroptera", "order"),
        ["Social"] = ("Chiroptera", "order"),
        ["?"] = ("Chiroptera", "order"),
        ["Mbart"] = ("Myotis", "genus"),
    };

    /// <inheritdoc />
    public string PlatformId => "inaturalist";

    /// <summary>Creates a publisher that obtains its API token from <paramref name="authenticator"/>.</summary>
    public INaturalistPublisher(
        INaturalistOptions options,
        HttpClient httpClient,
        INaturalistAuthenticator authenticator,
        ILogger<INaturalistPublisher>? logger = null)
        : this(options, httpClient, authenticator.EnsureAuthenticatedAsync, logger, null)
    {
    }

    internal INaturalistPublisher(
        INaturalistOptions options,
        HttpClient httpClient,
        Func<CancellationToken, Task<string>> getAccessToken,
        ILogger? logger = null,
        TimeProvider? time = null)
    {
        _options = options;
        _time = time ?? TimeProvider.System;
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
        string? taxonName = null;
        string? description = null;

        try
        {
            // iNaturalist's own rules first: no login, no request, nothing written for an entry it would refuse.
            if (INaturalistValidator.Validate(candidate, evidence, _options, _time.GetUtcNow()) is { } rejection)
            {
                return Result(rejection.Status, rejection.Message);
            }

            var jwt = await _getAccessToken(ct);

            var lookup = await ResolveTaxonAsync(candidate.ScientificName, jwt, ct);
            if (lookup.Taxon is not { } taxon)
            {
                return Result(PublishStatus.SkippedUnresolvedTaxon, lookup.Problem!);
            }

            taxonName = taxon.Name;
            var payload = BuildPayload(candidate, taxon.Id, taxon.Name);
            description = payload.Description;
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
                if (!options.Commit)
                {
                    return Result(PublishStatus.WouldResume, $"Dry run: would complete the incomplete observation {observationId} by attaching {Missing()}.");
                }

                _logger.LogInformation("Completing the incomplete iNaturalist observation {ObservationId} of {Species}", observationId, candidate.ScientificName);
            }
            else
            {
                if (!options.Commit)
                {
                    var filedAs = SameName(taxon.Name, candidate.ScientificName) ? "" : $" as {taxon.Name}";
                    return Result(PublishStatus.WouldCreate,
                        $"Dry run: would create an observation of {candidate.ScientificName}{filedAs} (taxon_id={taxon.Id}) and attach the spectrogram and the audio.");
                }

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

        string Missing() => photoAttached ? "the audio" : soundAttached ? "the spectrogram" : "the spectrogram and the audio";

        string Partial() => observationId is null
            ? ""
            : $" The observation {observationId} was already created; its evidence is incomplete.";

        PublishResult Result(PublishStatus status, string message, Exception? error = null, PublishStep? interruptedStep = null) => new()
        {
            Candidate = candidate,
            PlatformId = PlatformId,
            Status = status,
            TaxonName = taxonName,
            Description = description,
            ObservationId = observationId,
            Url = url,
            SpectrogramAttached = photoAttached,
            AudioAttached = soundAttached,
            Message = message,
            Error = error,
            InterruptedStep = interruptedStep,
        };
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Species need a binomial, a genus a single word; anything else is no name this package files an observation under.</summary>
    private async Task<TaxonLookup> ResolveTaxonAsync(string name, string jwt, CancellationToken ct)
    {
        if (BroaderTaxa.TryGetValue(name, out var broader))
        {
            return await _api.ResolveTaxonAsync(broader.Name, broader.Rank, jwt, ct);
        }

        var rank = name.Split(' ').Length switch { 1 => "genus", 2 => "species", _ => null };
        return rank is null
            ? new TaxonLookup(null, $"'{name}' is neither a species (two words) nor a genus (one word) name.")
            : await _api.ResolveTaxonAsync(name, rank, jwt, ct);
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

    internal ObservationPayload BuildPayload(ObservationCandidate candidate, int taxonId, string? taxonName = null) => new()
    {
        TaxonId = taxonId,
        ObservedOnString = candidate.ObservedAt.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Latitude = candidate.Latitude,
        Longitude = candidate.Longitude,
        // Always a string: v2 rejects null. The input format has no place name, so use the coordinates.
        PlaceGuess = string.Create(CultureInfo.InvariantCulture, $"{candidate.Latitude:F5}, {candidate.Longitude:F5}"),
        SpeciesGuess = candidate.ScientificName,
        Description = DescriptionBuilder.Build(candidate, _options.DescriptionPrefix, taxonName),
        TagList = _options.TagList,
    };
}
