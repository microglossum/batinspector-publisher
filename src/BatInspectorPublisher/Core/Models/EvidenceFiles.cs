namespace BatInspectorPublisher.Core.Models;

/// <summary>One evidence file, read once and checked by the orchestrator. Publishers upload exactly these bytes.</summary>
/// <param name="FileName">File name without directory, sent as the upload file name.</param>
/// <param name="Content">The complete file content.</param>
public sealed record EvidenceFile(string FileName, byte[] Content);

/// <summary>The evidence of one candidate: spectrogram image and audio recording.</summary>
/// <param name="Spectrogram">PNG image of the exemplar call.</param>
/// <param name="Audio">WAV recording of the exemplar call.</param>
public sealed record EvidenceFiles(EvidenceFile Spectrogram, EvidenceFile Audio);
