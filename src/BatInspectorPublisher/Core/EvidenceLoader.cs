using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using BatInspectorPublisher.Core.Validation;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Reads the evidence files of one candidate exactly once and has <see cref="EntryValidator"/> check them, so that what is
/// validated is what gets uploaded. Only one candidate's evidence is held in memory at a time.
/// </summary>
internal static class EvidenceLoader
{
    /// <summary>Outcome of loading: the evidence, or the skip status with the reason.</summary>
    internal readonly record struct Outcome(EvidenceFiles? Files, PublishStatus Status, string? Message);

    public static async Task<Outcome> LoadAsync(ObservationCandidate candidate, CancellationToken ct)
    {
        var png = await ReadAsync(candidate.SpectrogramPath, "spectrogram", EvidenceKind.Spectrogram, ct);
        if (png.Problem is not null)
        {
            return Rejected(png);
        }

        var wav = await ReadAsync(candidate.AudioPath, "audio", EvidenceKind.Audio, ct);
        if (wav.Problem is not null)
        {
            return Rejected(wav);
        }

        return new Outcome(new EvidenceFiles(png.File!, wav.File!), default, null);
    }

    private static Outcome Rejected(ReadOutcome read) => new(null, read.Status, read.Problem);

    private static async Task<ReadOutcome> ReadAsync(string path, string role, EvidenceKind kind, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return new(null, PublishStatus.SkippedMissingEvidence, $"Evidence file not found: {path}");
        }

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, PublishStatus.SkippedInvalidEvidence, $"The {role} file cannot be read: {path} ({ex.Message})");
        }

        if (EntryValidator.ValidateEvidenceContent(kind, content) is { } problem)
        {
            return new(null, PublishStatus.SkippedInvalidEvidence, $"The {role} file is {problem}: {path}");
        }

        return new(new EvidenceFile(Path.GetFileName(path), content), default, null);
    }

    private readonly record struct ReadOutcome(EvidenceFile? File, PublishStatus Status, string? Problem);
}
