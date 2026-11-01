using System.Globalization;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// Prototype of the NABU|naturgucker adapter, built step by step and best effort. Internal; not usable before the
/// platform's operator has confirmed the approach. Gaps of the platform are worked around in the adapter, not in <c>Core</c>.
/// <para>
/// One candidate: the platform's own rules, the species entry, a look at the user's own observations for the entry (the
/// stable identifier decides duplicate and resume), then, in a commit run, create and attach the photo. The audio is never
/// uploaded: the platform takes photos only. A write is never repeated; what is left half done is completed by the next run.
/// </para>
/// </summary>
internal sealed partial class NaturguckerPublisher : IObservationPublisher
{
    private readonly NaturguckerApiClient _api;
    private readonly ILogger _logger;
    private bool _loginChecked;

    internal NaturguckerPublisher(
        NaturguckerOptions options,
        HttpClient httpClient,
        ILogger? logger = null,
        Uri? baseAddress = null,
        TimeSpan? minRequestInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        options.ValidateForPublishing();
        _logger = logger ?? NullLogger.Instance;
        _api = new NaturguckerApiClient(httpClient, options, baseAddress, minRequestInterval, delay);
    }

    public string PlatformId => "naturgucker";

    [LoggerMessage(EventId = 5001, EventName = "ObservationResuming", Level = LogLevel.Information,
        Message = "Completing the incomplete naturgucker observation {ObservationId} of {Species}")]
    private static partial void LogResuming(ILogger logger, string? observationId, string species);

    [LoggerMessage(EventId = 5002, EventName = "ObservationCreated", Level = LogLevel.Information,
        Message = "Created naturgucker observation {ObservationId} of {Species}")]
    private static partial void LogCreated(ILogger logger, string? observationId, string species);

    Task<PublishResult> IObservationPublisher.PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct) =>
        PublishAsync(candidate, evidence, options, ct);

    internal async Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default)
    {
        string? taxonName = null;
        string? description = null;
        string? observationId = null;
        var photoAttached = false;
        var step = PublishStep.Preparation;

        try
        {
            // The platform's own rules first: no request for an entry it would refuse.
            if (!NaturguckerTaxa.TryResolve(candidate.ScientificName, out var taxon, out var rejection))
            {
                return Result(rejection.Status, rejection.Message);
            }

            taxonName = taxon.ToString();
            description = NaturguckerDescriptionBuilder.Build(candidate, taxon);

            var lookup = await _api.FindSpeciesAsync(taxon, ct).ConfigureAwait(false);
            if (lookup.Id is not { } speciesId)
            {
                return Result(PublishStatus.SkippedUnresolvedTaxon, $"naturgucker has no entry '{taxon}' for '{candidate.ScientificName}': {lookup.Problem}");
            }

            // The query below cannot tell a refused login from an empty result, so the login is checked first; once is enough
            // for this publisher (a refusal is not remembered: the next candidate asks again).
            if (!_loginChecked)
            {
                await _api.CheckLoginAsync(ct).ConfigureAwait(false);
                _loginChecked = true;
            }

            var entryId = NaturguckerEntryId.For(candidate);
            var observation = new NewObservation(candidate, speciesId, taxon.Uncertain, description, entryId);
            var existing = await _api.FindOwnObservationAsync(DateOnly.FromDateTime(candidate.ObservedAt.DateTime), entryId, ct).ConfigureAwait(false);

            if (existing is { } found)
            {
                observationId = found.HeadId.ToString(CultureInfo.InvariantCulture);
                photoAttached = found.HasPhoto;
                if (found.HasPhoto)
                {
                    return Result(PublishStatus.SkippedDuplicate, $"naturgucker already has this observation ({observationId}) with its photo.");
                }

                if (!options.Commit)
                {
                    return Result(PublishStatus.WouldResume, $"naturgucker has this observation ({observationId}) without its photo; a commit run would attach it.");
                }

                LogResuming(_logger, observationId, candidate.ScientificName);
                step = PublishStep.AttachSpectrogram;
                await _api.UploadPhotoAsync(observation, found.HeadId, evidence.Spectrogram, ct).ConfigureAwait(false);
                photoAttached = true;
                return Result(PublishStatus.Resumed, $"Attached the photo to the incomplete naturgucker observation {observationId}. The audio is not uploaded: the platform takes photos only.");
            }

            if (!options.Commit)
            {
                return Result(PublishStatus.WouldCreate, "A commit run would create this observation. The audio would not be uploaded: the platform takes photos only.");
            }

            step = PublishStep.CreateObservation;
            var headId = await _api.CreateObservationAsync(observation, ct).ConfigureAwait(false);
            observationId = headId.ToString(CultureInfo.InvariantCulture);
            step = PublishStep.AttachSpectrogram;
            await _api.UploadPhotoAsync(observation, headId, evidence.Spectrogram, ct).ConfigureAwait(false);
            photoAttached = true;
            LogCreated(_logger, observationId, candidate.ScientificName);
            return Result(PublishStatus.Created, "Created the observation and attached the photo. The audio is not uploaded: the platform takes photos only.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result(PublishStatus.Cancelled, Interrupted("Cancelled"), interruptedStep: step);
        }
        catch (Exception ex)
        {
            // Includes an OperationCanceledException that is not the caller's, such as an HttpClient timeout.
            // A failure before anything is written (the lookup, the look at the own observations, a refused login) would
            // end the same way for every following candidate, and without the look no duplicate could be ruled out: the run stops.
            var message = ex.Message + Partial();
            return Result(PublishStatus.Failed, message, ex, step) with { StopsRun = step == PublishStep.Preparation };
        }

        string Partial() => observationId is null ? "" : $" The observation {observationId} exists on naturgucker{(photoAttached ? "" : " without its photo; the next run completes it")}.";

        string Interrupted(string what) => observationId is not null
            ? $"{what} during {step}.{Partial()}"
            : step == PublishStep.CreateObservation
                ? $"{what} while the observation was being created; it may or may not exist on naturgucker."
                : $"{what} during {step}; nothing was created.";

        PublishResult Result(PublishStatus status, string message, Exception? error = null, PublishStep? interruptedStep = null) => new()
        {
            Candidate = candidate,
            PlatformId = PlatformId,
            Status = status,
            TaxonName = taxonName,
            Description = description,
            ObservationId = observationId,
            SpectrogramAttached = photoAttached,
            AudioAttached = false,
            Message = message,
            Error = error,
            InterruptedStep = interruptedStep,
        };
    }
}
