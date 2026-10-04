using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using BatInspectorPublisher.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Platform-agnostic control flow: per candidate, read and check the evidence once, then one publisher
/// call, with failure isolation and progress reporting. Everything platform-specific lives in the publisher.
/// </summary>
public sealed class ExportOrchestrator
{
    private readonly IObservationPublisher _publisher;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates an orchestrator that publishes through <paramref name="publisher"/>.</summary>
    public ExportOrchestrator(IObservationPublisher publisher, ILogger<ExportOrchestrator>? logger = null)
        : this(publisher, logger, null)
    {
    }

    internal ExportOrchestrator(IObservationPublisher publisher, ILogger<ExportOrchestrator>? logger, TimeProvider? time)
    {
        _publisher = publisher;
        _logger = logger ?? NullLogger<ExportOrchestrator>.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Publishes all candidates in order and returns one result per candidate. One failing candidate
    /// never stops the run.
    /// Cancellation does not throw: the run stops and returns the results so far, so nothing that was
    /// already published is lost. A candidate interrupted mid-way is the last result, with status
    /// <see cref="PublishStatus.Cancelled"/> and whatever was already created; candidates not yet started get no result.
    /// Check <paramref name="ct"/> to tell a cancelled run from a finished one.
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
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var result = await PublishOneAsync(candidate, options, ct);
            _logger.LogInformation("{Platform}: {Species} @ {ObservedAt:yyyy-MM-dd HH:mm:ss zzz} -> {Status}",
                result.PlatformId, candidate.ScientificName, candidate.ObservedAt, result.Status);
            results.Add(result);
            progress?.Report(result);

            if (result.Status == PublishStatus.Cancelled)
            {
                break;
            }
        }

        return results;
    }

    private async Task<PublishResult> PublishOneAsync(ObservationCandidate candidate, PublishOptions options, CancellationToken ct)
    {
        var evidenceLoaded = false;
        try
        {
            // The reader already judged candidates from a file; one built in code has not been judged yet.
            var issues = EntryValidator.ValidateValues(EntryValues.From(candidate), _time.GetUtcNow());
            if (issues.Count > 0)
            {
                return new PublishResult
                {
                    Candidate = candidate,
                    PlatformId = _publisher.PlatformId,
                    Status = PublishStatus.SkippedInvalidEntry,
                    Message = string.Join("; ", issues),
                };
            }

            var evidence = await EvidenceLoader.LoadAsync(candidate, ct);
            if (evidence.Files is null)
            {
                return new PublishResult
                {
                    Candidate = candidate,
                    PlatformId = _publisher.PlatformId,
                    Status = evidence.Status,
                    Message = evidence.Message,
                };
            }

            evidenceLoaded = true;
            return await _publisher.PublishAsync(candidate, evidence.Files, options, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A publisher is meant to return Cancelled itself; this covers one that throws instead.
            return new PublishResult
            {
                Candidate = candidate,
                PlatformId = _publisher.PlatformId,
                Status = PublishStatus.Cancelled,
                Message = evidenceLoaded ? "Cancelled." : "Cancelled while reading the evidence; nothing was created.",
                InterruptedStep = evidenceLoaded ? null : PublishStep.Preparation,
            };
        }
        catch (Exception ex)
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
}
