using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Publishes observations to one platform. Each adapter owns everything platform-specific:
/// authentication, species resolution, duplicate detection, payload shape and evidence upload.
/// There is deliberately no shared auth abstraction - platforms differ too much.
/// </summary>
public interface IObservationPublisher
{
    /// <summary>Stable lower-case platform id, e.g. "inaturalist".</summary>
    string PlatformId { get; }

    /// <summary>
    /// Publishes one candidate. Expected per-candidate problems (unknown species, duplicate,
    /// HTTP errors) are reported as a <see cref="PublishResult"/>, not thrown. Cancellation through
    /// <paramref name="ct"/> is reported as <see cref="PublishStatus.Cancelled"/> carrying whatever was
    /// already created on the platform; a publisher that throws <see cref="OperationCanceledException"/>
    /// instead is tolerated by <see cref="ExportOrchestrator"/>, but then that information is lost.
    /// </summary>
    /// <param name="candidate">The observation to publish.</param>
    /// <param name="evidence">The candidate's evidence, already read and checked. Upload exactly these bytes; do not re-read the files.</param>
    /// <param name="options">Publish options.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default);
}

/// <summary>Options for a publish call.</summary>
public sealed record PublishOptions
{
    /// <summary>
    /// When false (the default) nothing is written to the platform: the publisher only reports what
    /// it would do. Publishing is irreversible and public, so it must be requested explicitly.
    /// </summary>
    public bool Commit { get; init; }
}
