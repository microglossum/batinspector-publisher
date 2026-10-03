using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.Results;

/// <summary>Outcome of publishing one <see cref="ObservationCandidate"/>.</summary>
public enum PublishStatus
{
    /// <summary>The observation was created on the platform.</summary>
    Created,

    /// <summary>Dry run: the observation would be created; nothing was written.</summary>
    WouldCreate,

    /// <summary>The platform already has an equivalent observation of this user.</summary>
    SkippedDuplicate,

    /// <summary>The species name could not be resolved to a taxon on the platform.</summary>
    SkippedUnresolvedTaxon,

    /// <summary>The spectrogram or audio file does not exist; nothing is published without evidence.</summary>
    SkippedMissingEvidence,

    /// <summary>The spectrogram or audio file is unreadable, empty, or not a PNG / WAV file; nothing is published.</summary>
    SkippedInvalidEvidence,

    /// <summary>Publishing failed; see <see cref="PublishResult.Error"/>. May be partial, see <see cref="PublishResult.ObservationId"/>.</summary>
    Failed,

    /// <summary>
    /// An earlier run left an incomplete observation of this entry on the platform; this run attached
    /// the missing evidence to it (see <see cref="PublishResult.ObservationId"/>). No new observation was created.
    /// </summary>
    Resumed,

    /// <summary>
    /// The caller cancelled while this candidate was being published. May be partial: if
    /// <see cref="PublishResult.ObservationId"/> is set the observation exists on the platform and its evidence may be incomplete.
    /// </summary>
    Cancelled,
}

/// <summary>The stage of publishing one candidate, used to say where a <see cref="PublishStatus.Failed"/> or <see cref="PublishStatus.Cancelled"/> result stopped.</summary>
public enum PublishStep
{
    /// <summary>Reading the evidence, signing in, resolving the species and checking for duplicates. Nothing was written to the platform.</summary>
    Preparation,

    /// <summary>Creating the observation. Interrupted here the outcome is unknown: the platform may have created it without the caller learning its id.</summary>
    CreateObservation,

    /// <summary>Attaching the spectrogram to the observation.</summary>
    AttachSpectrogram,

    /// <summary>Attaching the audio to the observation.</summary>
    AttachAudio,
}

/// <summary>
/// Result of publishing one candidate. In-process .NET type for 0.x; a versioned serialized form
/// is deferred until a consumer needs to persist results.
/// </summary>
public sealed record PublishResult
{
    /// <summary>The candidate this result belongs to.</summary>
    public required ObservationCandidate Candidate { get; init; }

    /// <summary>Platform that produced the result (see <see cref="IObservationPublisher.PlatformId"/>).</summary>
    public required string PlatformId { get; init; }

    /// <summary>What happened.</summary>
    public required PublishStatus Status { get; init; }

    /// <summary>
    /// Platform-side observation id. Also set when the observation was already created before the result
    /// became <see cref="PublishStatus.Failed"/> or <see cref="PublishStatus.Cancelled"/>, and for
    /// <see cref="PublishStatus.Resumed"/> and a <see cref="PublishStatus.SkippedDuplicate"/> whose existing observation is known.
    /// </summary>
    public string? ObservationId { get; init; }

    /// <summary>Public URL of the observation, if the platform provides one.</summary>
    public string? Url { get; init; }

    /// <summary>True if the platform observation has the spectrogram attached (attached now, or already by an earlier run).</summary>
    public bool SpectrogramAttached { get; init; }

    /// <summary>True if the platform observation has the audio attached (attached now, or already by an earlier run).</summary>
    public bool AudioAttached { get; init; }

    /// <summary>Short human-readable (English) detail.</summary>
    public string? Message { get; init; }

    /// <summary>The exception behind a <see cref="PublishStatus.Failed"/> result.</summary>
    public Exception? Error { get; init; }

    /// <summary>For <see cref="PublishStatus.Failed"/> and <see cref="PublishStatus.Cancelled"/>: the stage that was interrupted, if known.</summary>
    public PublishStep? InterruptedStep { get; init; }
}
