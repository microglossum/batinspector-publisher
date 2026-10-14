using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Publishes observations to one platform. Each adapter owns everything platform-specific:
/// authentication, species resolution, duplicate detection, payload shape and evidence upload.
/// There is deliberately no shared auth abstraction - platforms differ too much.
/// <para>
/// A host creates a publisher (such as <c>INaturalistPublisher</c>) and hands it to <see cref="ExportOrchestrator"/>, the only
/// way to publish: the orchestrator checks every candidate and its evidence before the publisher sees it, so publishing
/// cannot bypass those checks. The interface cannot be implemented outside this package; a new platform is a new adapter here.
/// </para>
/// </summary>
public interface IObservationPublisher
{
    /// <summary>Stable lower-case platform id, e.g. "inaturalist".</summary>
    string PlatformId { get; }

    /// <summary>
    /// Publishes one candidate that <see cref="ExportOrchestrator"/> has already checked (platform-neutral values,
    /// evidence read and checked). Expected per-candidate problems (unknown species, duplicate,
    /// HTTP errors) are reported as a <see cref="PublishResult"/>, not thrown. Cancellation through
    /// <paramref name="ct"/> is reported as <see cref="PublishStatus.Cancelled"/> carrying whatever was
    /// already created on the platform; a publisher that throws <see cref="OperationCanceledException"/>
    /// instead is tolerated by <see cref="ExportOrchestrator"/>, but then that information is lost.
    /// <para>
    /// Re-runs must be safe: hosts re-run a file after an interruption, and the package relies on the publisher's duplicate
    /// check instead of keeping state. A repeated commit run of the same candidate must not create a second observation;
    /// it skips what an earlier run created (<see cref="PublishStatus.SkippedDuplicate"/>) or completes it
    /// (<see cref="PublishStatus.Resumed"/>). A publisher that cannot do this must say so in its own documentation.
    /// </para>
    /// </summary>
    /// <param name="candidate">The observation to publish.</param>
    /// <param name="evidence">The candidate's evidence, already read and checked. Upload exactly these bytes; do not re-read the files.</param>
    /// <param name="options">Publish options.</param>
    /// <param name="ct">Cancellation token.</param>
    internal Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default);
}
