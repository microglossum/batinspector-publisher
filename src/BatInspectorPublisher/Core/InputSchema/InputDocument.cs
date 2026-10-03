using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.InputSchema;

/// <summary>A parsed and validated BatInspector export file.</summary>
/// <param name="SchemaVersion">The <c>SchemaVersion</c> declared in the file.</param>
/// <param name="Candidates">One candidate per valid <c>DocumentFiles</c> entry, in file order.</param>
public sealed record InputDocument(int SchemaVersion, IReadOnlyList<ObservationCandidate> Candidates)
{
    /// <summary>
    /// Entries that failed validation, in file order. They are not in <see cref="Candidates"/> and are never
    /// published. Hosts should show them: the run itself does not report them.
    /// </summary>
    public IReadOnlyList<RejectedEntry> Rejected { get; init; } = [];

    /// <summary>
    /// Doubtful but accepted entries, in file order. These entries are in <see cref="Candidates"/> and are published;
    /// the warning tells the host something it may want to show (for example an ambiguous local time).
    /// </summary>
    public IReadOnlyList<EntryWarning> Warnings { get; init; } = [];
}

/// <summary>A remark about a <c>DocumentFiles</c> entry that was accepted.</summary>
/// <param name="Index">Zero-based position of the entry in <c>DocumentFiles</c>.</param>
/// <param name="Issue">What is doubtful.</param>
public sealed record EntryWarning(int Index, ValidationIssue Issue);

/// <summary>A <c>DocumentFiles</c> entry that failed validation and was left out of the candidates.</summary>
/// <param name="Index">Zero-based position of the entry in <c>DocumentFiles</c>.</param>
/// <param name="Issues">Every problem found in this entry.</param>
public sealed record RejectedEntry(int Index, IReadOnlyList<ValidationIssue> Issues);

/// <summary>One problem found in an input file.</summary>
/// <param name="Path">JSON path of the offending value, e.g. <c>DocumentFiles[2].Latitude</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ValidationIssue(string Path, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// Thrown when an input file as a whole is unusable: not well-formed JSON, wrong root, missing or unsupported
/// <c>SchemaVersion</c>, or no <c>DocumentFiles</c> array. Problems in single entries are not thrown, see <see cref="InputDocument.Rejected"/>.
/// </summary>
public sealed class InputSchemaException : Exception
{
    /// <summary>The problems found.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>Creates the exception from a non-empty list of issues.</summary>
    public InputSchemaException(IReadOnlyList<ValidationIssue> issues, Exception? inner = null)
        : base(BuildMessage(issues), inner)
    {
        Issues = issues;
    }

    private static string BuildMessage(IReadOnlyList<ValidationIssue> issues) =>
        $"Invalid BatInspector input file ({issues.Count} issue(s)): " + string.Join("; ", issues);
}
