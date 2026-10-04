using BatInspectorPublisher.Core.InputSchema;

namespace BatInspectorPublisher.Core.Results;

/// <summary>
/// Everything a run says about one input file in one place: the entries that were rejected, the warnings about accepted entries,
/// and the result per candidate. Show it to the user after a dry run, before the commit run.
/// </summary>
public sealed record RunReport
{
    /// <summary>Entries that failed validation and were never offered to the platform, in file order.</summary>
    public IReadOnlyList<RejectedEntry> Rejected { get; init; } = [];

    /// <summary>Remarks about accepted entries, in file order.</summary>
    public IReadOnlyList<EntryWarning> Warnings { get; init; } = [];

    /// <summary>One result per candidate that was started, in file order. A run that was cancelled has fewer results than candidates.</summary>
    public IReadOnlyList<PublishResult> Results { get; init; } = [];

    /// <summary>True when every candidate has a result and the run was not cancelled. False for a cancelled run: do not read the report as final.</summary>
    public bool IsComplete { get; init; }

    /// <summary>How many results have each status.</summary>
    public IReadOnlyDictionary<PublishStatus, int> CountByStatus =>
        Results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>The warnings about the entry behind <paramref name="result"/>; empty for a candidate built in code.</summary>
    public IEnumerable<EntryWarning> WarningsFor(PublishResult result) =>
        result.Candidate.EntryIndex is { } index ? Warnings.Where(w => w.Index == index) : [];
}
