using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// Placeholder for NABU|naturgucker. Blocked: there is no public API documentation, so neither the
/// auth nor the submission model is known until NABU's developers have been contacted. Internal and
/// intentionally empty; do not design anything here (or the shared interface) around guesses.
/// </summary>
internal sealed class NaturguckerPublisher : IObservationPublisher
{
    public string PlatformId => "naturgucker";

    public Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default) =>
        throw new NotImplementedException("The naturgucker adapter is not implemented: waiting for API information from NABU.");
}
