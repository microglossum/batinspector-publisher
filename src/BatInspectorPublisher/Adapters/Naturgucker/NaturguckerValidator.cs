using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// The platform's own rules for an observation, checked before any login or request. The orchestrator has already run the
/// platform-neutral checks; this class adds only what this platform is stricter about. More rules (evidence, limits) are added
/// as the prototype learns them.
/// </summary>
internal static class NaturguckerValidator
{
    /// <summary>Why a candidate must not be published, as the status and the English message the result carries.</summary>
    internal readonly record struct Rejection(PublishStatus Status, string Message);

    /// <summary>Returns null when the candidate passes; otherwise the reason it is left out.</summary>
    public static Rejection? Validate(ObservationCandidate candidate) =>
        NaturguckerTaxa.TryResolve(candidate.ScientificName, out _, out var rejection) ? null : rejection;
}
