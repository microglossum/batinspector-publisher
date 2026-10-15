namespace BatInspectorPublisher.Core;

/// <summary>Options for a publish call.</summary>
public sealed record PublishOptions
{
    /// <summary>
    /// When false (the default) nothing is written to the platform: the publisher only reports what
    /// it would do. A publisher must not skip a read-only step that decides the outcome (such as the duplicate check) in a dry run,
    /// or the preview misreports it. Publishing is irreversible and public, so it must be requested explicitly.
    /// <para>
    /// A dry run goes through every step except the writes and reports what a commit run would do. It is optional.
    /// <c>InputDocument.Rejected</c> and <c>InputDocument.Warnings</c> do not depend on it; <see cref="ExportOrchestrator"/> reports them in a
    /// <see cref="Results.RunReport"/>, a run over the candidates alone does not, so a host that skips the dry run should show them to the user before it commits.
    /// </para>
    /// </summary>
    public bool Commit { get; init; }
}
