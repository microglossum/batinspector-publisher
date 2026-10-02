using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Platform-agnostic control flow: pre-flight evidence check, one publisher call per candidate,
/// failure isolation, progress reporting. Everything platform-specific lives in the publisher.
/// </summary>
public sealed class ExportOrchestrator
{
    private readonly IObservationPublisher _publisher;
    private readonly ILogger _logger;

    /// <summary>Creates an orchestrator that publishes through <paramref name="publisher"/>.</summary>
    public ExportOrchestrator(IObservationPublisher publisher, ILogger<ExportOrchestrator>? logger = null)
    {
        _publisher = publisher;
        _logger = logger ?? NullLogger<ExportOrchestrator>.Instance;
    }

    /// <summary>
    /// Publishes all candidates in order and returns one result per candidate. One failing candidate
    /// never stops the run. Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="candidates">Candidates to publish.</param>
    /// <param name="options">Publish options (dry run unless <see cref="PublishOptions.Commit"/> is set).</param>
    /// <param name="progress">Receives each result as soon as it is available.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IReadOnlyList<PublishResult>> RunAsync(
        IEnumerable<ObservationCandidate> candidates,
        PublishOptions options,
        IProgress<PublishResult>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<PublishResult>();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await PublishOneAsync(candidate, options, ct);
            _logger.LogInformation("{Platform}: {Species} @ {ObservedAt:yyyy-MM-dd HH:mm:ss} -> {Status}",
                result.PlatformId, candidate.ScientificName, candidate.ObservedAt, result.Status);
            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }

    private async Task<PublishResult> PublishOneAsync(ObservationCandidate candidate, PublishOptions options, CancellationToken ct)
    {
        var missing = MissingEvidence(candidate);
        if (missing is not null)
        {
            return new PublishResult
            {
                Candidate = candidate,
                PlatformId = _publisher.PlatformId,
                Status = PublishStatus.SkippedMissingEvidence,
                Message = $"Evidence file not found: {missing}",
            };
        }

        try
        {
            return await _publisher.PublishAsync(candidate, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PublishResult
            {
                Candidate = candidate,
                PlatformId = _publisher.PlatformId,
                Status = PublishStatus.Failed,
                Message = ex.Message,
                Error = ex,
            };
        }
    }

    private static string? MissingEvidence(ObservationCandidate candidate) =>
        !File.Exists(candidate.SpectrogramPath) ? candidate.SpectrogramPath
        : !File.Exists(candidate.AudioPath) ? candidate.AudioPath
        : null;
}
