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
public sealed partial class INaturalistPublisher : IObservationPublisher
{
    private readonly INaturalistOptions _options;
    private readonly INaturalistApiClient _api;
    private readonly Func<CancellationToken, Task<string>> _getAccessToken;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Every taxon this package files an observation under must be a bat: a name that matches only outside the bats
    /// (a homonym in another group) is skipped, never published. Resolved live by name like every other taxon.
    /// </summary>
    private static readonly (string Name, string Rank) Bats = ("Chiroptera", "order");

    /// <summary>
    /// BatInspector group and uncertain-call values that are not taxon names, and the broader taxon each is filed under.
    /// The original value stays in <c>species_guess</c> and is named in the description. Anything else that does not resolve is skipped, never
    /// filed under a guess: a typo filed under Chiroptera would be public and a corrected re-run would create a second observation.
    /// "todo" (not yet reviewed in BatInspector) is deliberately not listed.
    /// </summary>
    private static readonly Dictionary<string, (string Name, string Rank)> BroaderTaxa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nyctaloid"] = Bats,
        ["Social"] = Bats,
        ["?"] = Bats,
        ["Mbart"] = ("Myotis", "genus"),
    };

    /// <inheritdoc />
    public string PlatformId => "inaturalist";

    /// <summary>Creates a publisher that obtains its API token from <paramref name="authenticator"/>.</summary>
    /// <remarks>
    /// The timeout of <paramref name="httpClient"/> (default 100 s) also limits the evidence uploads, which are never
    /// repeated after a timeout; on slow lines pass a client with a larger timeout.
    /// </remarks>
    /// <exception cref="ArgumentException"><see cref="INaturalistOptions.TagList"/> exceeds iNaturalist's limits, or a retry or pacing option is out of range.</exception>
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
        TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        options.ValidateForPublishing();
        _options = options;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _api = new INaturalistApiClient(options, httpClient, _logger, _time, delay);
        _getAccessToken = getAccessToken;
    }

    [LoggerMessage(EventId = 4001, EventName = "ObservationResuming", Level = LogLevel.Information,
        Message = "Completing the incomplete iNaturalist observation {ObservationId} of {Species}")]
    private static partial void LogResuming(ILogger logger, string? observationId, string species);

    [LoggerMessage(EventId = 4002, EventName = "ObservationCreated", Level = LogLevel.Information,
        Message = "Created iNaturalist observation {ObservationId} of {Species}")]
    private static partial void LogCreated(ILogger logger, string? observationId, string species);

    Task<PublishResult> IObservationPublisher.PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct) =>
        PublishAsync(candidate, evidence, options, ct);

    /// <summary>
    /// Publishes one candidate that the orchestrator has checked; see <see cref="IObservationPublisher"/>.
    /// Two steps: <see cref="PlanAsync"/> decides with reads only what a commit run would do, and
    /// <see cref="ExecuteAsync"/>, the only place that writes, does it. The commit check sits between them, once.
    /// </summary>
    internal async Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default)
    {
        var state = new PublishState();
        try
        {
            var plan = await PlanAsync(candidate, evidence, state, ct);
            var outcome = plan switch
            {
                Done done => done,
                WritePlan write when !options.Commit => new Done(write.DryRunStatus, write.DryRunMessage),
                WritePlan write => await ExecuteAsync(candidate, evidence, write, state, ct),
                _ => throw new InvalidOperationException($"Unknown plan {plan.GetType().Name}."),
            };
            return Result(outcome.Status, outcome.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result(PublishStatus.Cancelled, Interrupted("Cancelled"), interruptedStep: state.Step);
        }
        catch (Exception ex)
        {
            // Includes an OperationCanceledException that is not the caller's, such as an HttpClient timeout.
            if (state.SigningIn)
            {
                return Result(PublishStatus.Failed,
                    ex.Message + " Without a login nothing can be published, so the run stops here.", ex, state.Step) with
                { StopsRun = true };
            }

            return Result(PublishStatus.Failed, ex.Message + state.Partial(), ex, state.Step);
        }

        string Interrupted(string what) => state.ObservationId is not null
            ? $"{what} during {state.Step}.{state.Partial()}"
            : state.Step == PublishStep.CreateObservation
                ? $"{what} while the observation was being created; it may or may not exist on iNaturalist."
                : $"{what} during {state.Step}; nothing was created.";

        PublishResult Result(PublishStatus status, string message, Exception? error = null, PublishStep? interruptedStep = null) => new()
        {
            Candidate = candidate,
            PlatformId = PlatformId,
            Status = status,
            TaxonName = state.TaxonName,
            Description = state.Description,
            ObservationId = state.ObservationId,
            Url = state.Url,
            SpectrogramAttached = state.PhotoAttached,
            AudioAttached = state.SoundAttached,
            Message = message,
            Error = error,
            InterruptedStep = interruptedStep,
        };
    }

    /// <summary>
    /// Validation, login, taxon, payload and duplicate check: everything that decides the outcome, with reads only.
    /// Returns either the final outcome (rejected, unresolved, duplicate) or the writes a commit run makes.
    /// </summary>
    private async Task<Plan> PlanAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishState state, CancellationToken ct)
    {
        // iNaturalist's own rules first (the orchestrator has run the platform-neutral ones): no login, no request,
        // nothing written for an entry it would refuse.
        if (INaturalistValidator.Validate(candidate, evidence, _options, _time.GetUtcNow()) is { } rejection)
        {
            return new Done(rejection.Status, rejection.Message);
        }

        // A login that fails here would fail (and may open the browser) for every following candidate: it stops the run.
        state.SigningIn = true;
        var jwt = await _getAccessToken(ct);
        state.SigningIn = false;

        var lookup = await ResolveTaxonAsync(candidate.ScientificName, jwt, ct);
        if (lookup.Taxon is not { } taxon)
        {
            return new Done(PublishStatus.SkippedUnresolvedTaxon, lookup.Problem!);
        }

        state.TaxonName = taxon.Name;
        var payload = BuildPayload(candidate, taxon.Id, taxon.Name);
        state.Description = payload.Description;
        var userId = await _api.GetAuthenticatedUserIdAsync(jwt, ct);
        var existing = await _api.FindExistingObservationsAsync(
            taxon.Id, userId, DateOnly.FromDateTime(candidate.ObservedAt.DateTime), candidate.Latitude, candidate.Longitude, jwt, ct);

        if (existing.TotalResults == 0)
        {
            var filedAs = SameName(taxon.Name, candidate.ScientificName) ? "" : $" as {taxon.Name}";
            return new CreatePlan(payload, jwt,
                $"Dry run: would create an observation of {candidate.ScientificName}{filedAs} (taxon_id={taxon.Id}) and attach the spectrogram and the audio.");
        }

        var match = existing.Results.FirstOrDefault(o => IsIncompleteOwnObservation(o, payload.Description));
        if (match is null)
        {
            if (existing.Results.FirstOrDefault(o => o.Id != 0 || o.Uuid.Length > 0) is { } known)
            {
                state.Found(known);
            }

            return new Done(PublishStatus.SkippedDuplicate, "An equivalent observation already exists on iNaturalist.");
        }

        // An earlier run created this observation and stopped before its evidence was complete.
        // Only an observation whose description is exactly what this entry produces is completed;
        // anything else of the user's stays untouched.
        state.Found(match);
        return new ResumePlan(match.Uuid, jwt,
            $"Dry run: would complete the incomplete observation {state.ObservationId} by attaching {state.Missing()}.");
    }

    /// <summary>The writes of a commit run: create the observation (or take the incomplete one), then attach what is missing.</summary>
    private async Task<Done> ExecuteAsync(ObservationCandidate candidate, EvidenceFiles evidence, WritePlan plan, PublishState state, CancellationToken ct)
    {
        string uuid;
        if (plan is ResumePlan resume)
        {
            uuid = resume.Uuid;
            LogResuming(_logger, state.ObservationId, candidate.ScientificName);
        }
        else
        {
            state.Step = PublishStep.CreateObservation;
            var created = await _api.CreateObservationAsync(((CreatePlan)plan).Payload, plan.Jwt, ct);
            uuid = created.Uuid;
            (state.ObservationId, state.Url) = Identify(created.Id, created.Uuid);
            // Logged at once: if anything after this fails, this line is the trail to the observation.
            LogCreated(_logger, state.ObservationId, candidate.ScientificName);
        }

        if (!state.PhotoAttached)
        {
            state.Step = PublishStep.AttachSpectrogram;
            await _api.AttachPhotoAsync(uuid, evidence.Spectrogram, plan.Jwt, ct);
            state.PhotoAttached = true;
        }

        if (!state.SoundAttached)
        {
            state.Step = PublishStep.AttachAudio;
            await _api.AttachSoundAsync(uuid, evidence.Audio, plan.Jwt, ct);
            state.SoundAttached = true;
        }

        return plan is ResumePlan
            ? new Done(PublishStatus.Resumed, $"Completed the incomplete observation {state.ObservationId}.")
            : new Done(PublishStatus.Created, $"Created observation {state.ObservationId}.");
    }

    /// <summary>What <see cref="PlanAsync"/> decided.</summary>
    private abstract record Plan;

    /// <summary>The final outcome; nothing is (or was) written.</summary>
    private sealed record Done(PublishStatus Status, string Message) : Plan;

    /// <summary>Writes a commit run makes, and what a dry run reports instead.</summary>
    private abstract record WritePlan(string Jwt, PublishStatus DryRunStatus, string DryRunMessage) : Plan;

    private sealed record CreatePlan(ObservationPayload Payload, string Jwt, string DryRunMessage)
        : WritePlan(Jwt, PublishStatus.WouldCreate, DryRunMessage);

    private sealed record ResumePlan(string Uuid, string Jwt, string DryRunMessage)
        : WritePlan(Jwt, PublishStatus.WouldResume, DryRunMessage);

    /// <summary>
    /// What is known about one candidate's observation so far. Kept outside the steps so that a failure or a cancellation
    /// at any point reports what already exists on iNaturalist.
    /// </summary>
    private sealed class PublishState
    {
        public PublishStep Step { get; set; } = PublishStep.Preparation;
        public bool SigningIn { get; set; }
        public string? TaxonName { get; set; }
        public string? Description { get; set; }
        public string? ObservationId { get; set; }
        public string? Url { get; set; }
        public bool PhotoAttached { get; set; }
        public bool SoundAttached { get; set; }

        /// <summary>Takes over an observation that already exists on iNaturalist.</summary>
        public void Found(ExistingObservation observation)
        {
            (ObservationId, Url) = Identify(observation.Id, observation.Uuid);
            PhotoAttached = observation.Photos is { Count: > 0 };
            SoundAttached = observation.Sounds is { Count: > 0 };
        }

        public string Missing() => PhotoAttached ? "the audio" : SoundAttached ? "the spectrogram" : "the spectrogram and the audio";

        /// <summary>What exists on iNaturalist after an interruption, as a sentence to append; empty when nothing was written.</summary>
        public string Partial() => ObservationId is not null
            ? $" The observation {ObservationId} was already created; its evidence is incomplete."
            : Step == PublishStep.CreateObservation
                ? " The observation may or may not exist on iNaturalist; a later run finds it and completes it."
                : "";
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Species need a binomial, a genus a single word; anything else is no name this package files an observation under.
    /// Only a bat is accepted (see <see cref="Bats"/>).
    /// </summary>
    private async Task<TaxonLookup> ResolveTaxonAsync(string name, string jwt, CancellationToken ct)
    {
        if (BroaderTaxa.TryGetValue(name, out var broader))
        {
            return await _api.ResolveTaxonAsync(broader.Name, broader.Rank, jwt, ct, Bats);
        }

        var rank = ScientificName.WordCount(name) switch { 1 => "genus", 2 => "species", _ => null };
        return rank is null
            ? new TaxonLookup(null, $"'{name}' is neither a species (two words) nor a genus (one word) name.")
            : await _api.ResolveTaxonAsync(name, rank, jwt, ct, Bats);
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
