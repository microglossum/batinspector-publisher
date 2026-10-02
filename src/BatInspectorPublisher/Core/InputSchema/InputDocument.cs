using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.InputSchema;

/// <summary>A parsed and validated BatInspector export file.</summary>
/// <param name="SchemaVersion">The <c>SchemaVersion</c> declared in the file.</param>
/// <param name="Candidates">One candidate per <c>DocumentFiles</c> entry, in file order.</param>
public sealed record InputDocument(int SchemaVersion, IReadOnlyList<ObservationCandidate> Candidates);

/// <summary>One problem found in an input file.</summary>
/// <param name="Path">JSON path of the offending value, e.g. <c>DocumentFiles[2].Latitude</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ValidationIssue(string Path, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>Thrown when an input file is malformed, has an unsupported version, or fails validation.</summary>
public sealed class InputSchemaException : Exception
{
    /// <summary>All problems found (the reader reports every issue, not just the first).</summary>
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
