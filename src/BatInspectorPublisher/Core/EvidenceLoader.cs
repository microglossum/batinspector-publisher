using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Core;

/// <summary>
/// Reads the evidence files of one candidate exactly once and checks them, so that what is
/// validated is what gets uploaded. Only one candidate's evidence is held in memory at a time.
/// </summary>
internal static class EvidenceLoader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Outcome of loading: the evidence, or the skip status with the reason.</summary>
    internal readonly record struct Outcome(EvidenceFiles? Files, PublishStatus Status, string? Message);

    public static async Task<Outcome> LoadAsync(ObservationCandidate candidate, CancellationToken ct)
    {
        var png = await ReadAsync(candidate.SpectrogramPath, "spectrogram", IsPng, "not a PNG image", ct);
        if (png.Problem is not null)
        {
            return Rejected(png);
        }

        var wav = await ReadAsync(candidate.AudioPath, "audio", IsWav, "not a WAV recording", ct);
        if (wav.Problem is not null)
        {
            return Rejected(wav);
        }

        return new Outcome(new EvidenceFiles(png.File!, wav.File!), default, null);
    }

    private static Outcome Rejected(ReadOutcome read) => new(null, read.Status, read.Problem);

    private static async Task<ReadOutcome> ReadAsync(string path, string role, Func<byte[], bool> hasSignature, string signatureProblem, CancellationToken ct)
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

        if (content.Length == 0)
        {
            return new(null, PublishStatus.SkippedInvalidEvidence, $"The {role} file is empty: {path}");
        }

        if (!hasSignature(content))
        {
            return new(null, PublishStatus.SkippedInvalidEvidence, $"The {role} file is {signatureProblem}: {path}");
        }

        return new(new EvidenceFile(Path.GetFileName(path), content), default, null);
    }

    private static bool IsPng(byte[] content) => content.AsSpan().StartsWith(PngSignature);

    // RIFF container with a WAVE form type: "RIFF" <size> "WAVE".
    private static bool IsWav(byte[] content) =>
        content.Length >= 12
        && content.AsSpan(0, 4).SequenceEqual("RIFF"u8)
        && content.AsSpan(8, 4).SequenceEqual("WAVE"u8);

    private readonly record struct ReadOutcome(EvidenceFile? File, PublishStatus Status, string? Problem);
}
