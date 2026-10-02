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

    /// <summary>Publishing failed; see <see cref="PublishResult.Error"/>. May be partial, see <see cref="PublishResult.ObservationId"/>.</summary>
    Failed,
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

    /// <summary>Platform-side observation id. Also set when <see cref="Status"/> is <see cref="PublishStatus.Failed"/> after the observation was already created.</summary>
    public string? ObservationId { get; init; }

    /// <summary>Public URL of the created observation, if the platform provides one.</summary>
    public string? Url { get; init; }

    /// <summary>True if the spectrogram was attached to the platform observation.</summary>
    public bool SpectrogramAttached { get; init; }

    /// <summary>True if the audio was attached to the platform observation.</summary>
    public bool AudioAttached { get; init; }

    /// <summary>Short human-readable (English) detail.</summary>
    public string? Message { get; init; }

    /// <summary>The exception behind a <see cref="PublishStatus.Failed"/> result.</summary>
    public Exception? Error { get; init; }
}
