using BatInspectorPublisher.Core.InputSchema;
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
public sealed partial class ExportOrchestrator
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
    /// Publishes all candidates in order and returns one result per candidate that was started. A candidate that fails for
    /// its own reasons does not stop the run. A failure that would repeat for every candidate (the platform login could not
    /// be completed) stops it after that candidate's <see cref="PublishStatus.Failed"/> result, like a cancellation: the
    /// candidates not yet started get no result, and the next run tries again.
    /// Cancellation does not throw: the run stops and returns the results so far, so nothing that was
    /// already published is lost. A candidate interrupted mid-way is the last result, with status
    /// <see cref="PublishStatus.Cancelled"/> and whatever was already created; candidates not yet started get no result.
    /// Check <paramref name="ct"/> to tell a cancelled run from a finished one.
    /// <para>
    /// A candidate built in code is checked like an entry of the input file and its name normalized as the reader does;
    /// the result then carries the normalized candidate.
    /// </para>
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

            var result = await PublishOneAsync(candidate, options, ct).ConfigureAwait(false);
            if (result.Status == PublishStatus.Failed)
            {
                LogCandidateFailed(_logger, result.Error, result.PlatformId, candidate.ScientificName, candidate.ObservedAt, result.Message);
            }
            else
            {
                LogCandidateProcessed(_logger, result.PlatformId, candidate.ScientificName, candidate.ObservedAt, result.Status, result.Message);
            }

            results.Add(result);
            progress?.Report(result);

            if (result.Status == PublishStatus.Cancelled || result.StopsRun)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Publishes the candidates of <paramref name="document"/> like <see cref="RunAsync(IEnumerable{ObservationCandidate}, PublishOptions, IProgress{PublishResult}?, CancellationToken)"/>
    /// and returns one <see cref="RunReport"/> that also carries the document's rejected entries and warnings.
    /// After a dry run, show the report to the user before you call this again with <see cref="PublishOptions.Commit"/>.
    /// </summary>
    /// <param name="document">The parsed input file.</param>
    /// <param name="options">Publish options (dry run unless <see cref="PublishOptions.Commit"/> is set).</param>
    /// <param name="progress">Receives each result as soon as it is available.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RunReport> RunAsync(
        InputDocument document,
        PublishOptions options,
        IProgress<PublishResult>? progress = null,
        CancellationToken ct = default)
    {
        var results = await RunAsync(document.Candidates, options, progress, ct).ConfigureAwait(false);
        return new RunReport
        {
            Rejected = document.Rejected,
            Warnings = document.Warnings,
            Results = results,
            IsComplete = results.Count == document.Candidates.Count && results.All(r => r.Status != PublishStatus.Cancelled),
        };
    }

    [LoggerMessage(EventId = 1001, EventName = "CandidateProcessed", Level = LogLevel.Information,
        Message = "{Platform}: {Species} @ {ObservedAt:yyyy-MM-dd HH:mm:ss zzz} -> {Status}: {Message}")]
    private static partial void LogCandidateProcessed(ILogger logger, string platform, string species, DateTimeOffset observedAt, PublishStatus status, string? message);

    [LoggerMessage(EventId = 1002, EventName = "CandidateFailed", Level = LogLevel.Error,
        Message = "{Platform}: {Species} @ {ObservedAt:yyyy-MM-dd HH:mm:ss zzz} failed: {Message}")]
    private static partial void LogCandidateFailed(ILogger logger, Exception? error, string platform, string species, DateTimeOffset observedAt, string? message);

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

            // The reader normalizes names from a file; one built in code may differ in case or spacing.
            var name = ScientificName.Normalize(candidate.ScientificName);
            if (name != candidate.ScientificName)
            {
                candidate = candidate with { ScientificName = name };
            }

            var evidence = await EvidenceLoader.LoadAsync(candidate, ct).ConfigureAwait(false);
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
            return await _publisher.PublishAsync(candidate, evidence.Files, options, ct).ConfigureAwait(false);
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
