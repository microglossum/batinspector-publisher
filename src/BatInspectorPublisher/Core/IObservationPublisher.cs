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
    /// HTTP errors) are reported as a <see cref="PublishResult"/>, not thrown. Cancellation is
    /// reported by throwing <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<PublishResult> PublishAsync(ObservationCandidate candidate, PublishOptions options, CancellationToken ct = default);
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
