using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Core;

public class ExportOrchestratorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("orchestrator-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] WavBytes = [.. "RIFF"u8, 4, 0, 0, 0, .. "WAVE"u8, 1, 2];

    private ObservationCandidate Candidate(string species, bool withEvidence = true, byte[]? png = null, byte[]? wav = null)
    {
        var pngPath = Path.Combine(_dir, species + ".png");
        var wavPath = Path.Combine(_dir, species + ".wav");
        if (withEvidence)
        {
            File.WriteAllBytes(pngPath, png ?? PngBytes);
            File.WriteAllBytes(wavPath, wav ?? WavBytes);
        }

        return new ObservationCandidate
        {
            ScientificName = species,
            ObservedAt = new DateTimeOffset(2026, 6, 13, 4, 0, 0, TimeSpan.FromHours(2)),
            Latitude = 50,
            Longitude = 8,
            SpectrogramPath = pngPath,
            AudioPath = wavPath,
        };
    }

    private sealed class FakePublisher(Func<ObservationCandidate, PublishOptions, CancellationToken, PublishResult> publish) : IObservationPublisher
    {
        public List<(ObservationCandidate Candidate, EvidenceFiles Evidence, PublishOptions Options)> Calls { get; } = [];
        public string PlatformId => "fake";

        public Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default)
        {
            Calls.Add((candidate, evidence, options));
            return Task.FromResult(publish(candidate, options, ct));
        }
    }

    private static PublishResult Created(ObservationCandidate c) =>
        new() { Candidate = c, PlatformId = "fake", Status = PublishStatus.Created };

    [Fact]
    public async Task RunAsync_ReturnsOneResultPerCandidate_InOrder()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));
        var candidates = new[] { Candidate("A a"), Candidate("B b") };

        var results = await new ExportOrchestrator(publisher).RunAsync(candidates, new PublishOptions { Commit = true });

        Assert.Equal(["A a", "B b"], results.Select(r => r.Candidate.ScientificName));
        Assert.All(results, r => Assert.Equal(PublishStatus.Created, r.Status));
    }

    [Fact]
    public async Task RunAsync_PassesOptionsToPublisher_AndDefaultsToDryRun()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));

        await new ExportOrchestrator(publisher).RunAsync([Candidate("A a")], new PublishOptions());

        Assert.False(publisher.Calls.Single().Options.Commit);
    }

    [Fact]
    public async Task RunAsync_MissingEvidence_IsSkippedWithoutCallingPublisher()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync([Candidate("A a", withEvidence: false)], new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedMissingEvidence, results.Single().Status);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    public async Task RunAsync_CandidateBuiltInCodeBreaksARule_IsSkippedWithoutReadingEvidenceOrCallingPublisher()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));
        var bad = Candidate("A a") with { Latitude = 0, Longitude = 0, SpectrogramPath = "relative/a.png" };

        var results = await new ExportOrchestrator(publisher).RunAsync([bad, Candidate("B b")], new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedInvalidEntry, results[0].Status);
        Assert.Contains("GPS", results[0].Message);
        Assert.Contains("absolute", results[0].Message);
        Assert.Equal(PublishStatus.Created, results[1].Status);
        Assert.Equal("B b", publisher.Calls.Single().Candidate.ScientificName);
    }

    [Fact]
    public async Task RunAsync_CandidateInTheFuture_IsJudgedAgainstTheOrchestratorsClock()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));
        var clock = new FixedTime(new DateTimeOffset(2026, 6, 13, 1, 0, 0, TimeSpan.Zero));

        var results = await new ExportOrchestrator(publisher, null, clock).RunAsync([Candidate("A a")], new PublishOptions());

        Assert.Equal(PublishStatus.SkippedInvalidEntry, results.Single().Status);
        Assert.Empty(publisher.Calls);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task RunAsync_HandsTheValidatedBytesAndFileNamesToThePublisher()
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));

        await new ExportOrchestrator(publisher).RunAsync([Candidate("A a")], new PublishOptions());

        var evidence = publisher.Calls.Single().Evidence;
        Assert.Equal(PngBytes, evidence.Spectrogram.Content);
        Assert.Equal("A a.png", evidence.Spectrogram.FileName);
        Assert.Equal(WavBytes, evidence.Audio.Content);
        Assert.Equal("A a.wav", evidence.Audio.FileName);
    }

    public static TheoryData<string, byte[]?, byte[]?> InvalidEvidence => new()
    {
        { "empty spectrogram", Array.Empty<byte>(), null },
        { "empty audio", null, Array.Empty<byte>() },
        { "spectrogram is not a PNG", "not a png"u8.ToArray(), null },
        { "audio is not a WAV", null, "not a wav"u8.ToArray() },
        { "audio is a PNG", null, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { "RIFF but not WAVE", null, [.. "RIFF"u8, 4, 0, 0, 0, .. "AVI "u8] },
    };

    [Theory]
    [MemberData(nameof(InvalidEvidence))]
    public async Task RunAsync_InvalidEvidence_IsSkippedWithoutCallingPublisher(string _, byte[]? png, byte[]? wav)
    {
        var publisher = new FakePublisher((c, _, _) => Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a", png: png, wav: wav), Candidate("B b")], new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.SkippedInvalidEvidence, results[0].Status);
        Assert.Contains(_dir, results[0].Message);
        Assert.Equal(PublishStatus.Created, results[1].Status);
        Assert.Equal("B b", publisher.Calls.Single().Candidate.ScientificName);
    }

    [Fact]
    public async Task RunAsync_PublisherThrows_BecomesFailedAndRunContinues()
    {
        var publisher = new FakePublisher((c, _, _) =>
            c.ScientificName == "Bad bad" ? throw new InvalidOperationException("boom") : Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("Bad bad"), Candidate("Good good")], new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, results[0].Status);
        Assert.Equal("boom", results[0].Error!.Message);
        Assert.Equal(PublishStatus.Created, results[1].Status);
    }

    [Fact]
    public async Task RunAsync_CancelledBetweenCandidates_ReturnsTheResultsSoFarWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        var publisher = new FakePublisher((c, _, _) =>
        {
            cts.Cancel();
            return Created(c);
        });

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a"), Candidate("B b")], new PublishOptions { Commit = true }, ct: cts.Token);

        Assert.Equal(PublishStatus.Created, results.Single().Status);
        Assert.Single(publisher.Calls);
    }

    [Fact]
    public async Task RunAsync_PublisherReportsCancelled_KeepsItsDetailsStopsAndReportsProgress()
    {
        var reported = new List<PublishResult>();
        var publisher = new FakePublisher((c, _, _) => c.ScientificName == "B b"
            ? new PublishResult
            {
                Candidate = c,
                PlatformId = "fake",
                Status = PublishStatus.Cancelled,
                ObservationId = "42",
                InterruptedStep = PublishStep.AttachAudio,
                SpectrogramAttached = true,
            }
            : Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a"), Candidate("B b"), Candidate("C c")], new PublishOptions { Commit = true }, new SynchronousProgress(reported.Add));

        Assert.Equal(["A a", "B b"], results.Select(r => r.Candidate.ScientificName));
        Assert.Equal("42", results[1].ObservationId);
        Assert.Equal(PublishStep.AttachAudio, results[1].InterruptedStep);
        Assert.Equal(2, reported.Count);
        Assert.Equal(2, publisher.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_PublisherThrowsOperationCanceled_BecomesCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        var publisher = new FakePublisher((_, _, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a"), Candidate("B b")], new PublishOptions { Commit = true }, ct: cts.Token);

        Assert.Equal(PublishStatus.Cancelled, results.Single().Status);
        Assert.Single(publisher.Calls);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_ReturnsNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var publisher = new FakePublisher((c, _, _) => Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync([Candidate("A a")], new PublishOptions { Commit = true }, ct: cts.Token);

        Assert.Empty(results);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    public async Task RunAsync_OperationCanceledWithoutCallerCancellation_IsAFailureAndTheRunContinues()
    {
        var publisher = new FakePublisher((c, _, _) =>
            c.ScientificName == "A a" ? throw new TaskCanceledException("HTTP timeout") : Created(c));

        var results = await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a"), Candidate("B b")], new PublishOptions { Commit = true });

        Assert.Equal(PublishStatus.Failed, results[0].Status);
        Assert.Equal(PublishStatus.Created, results[1].Status);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressPerCandidate()
    {
        var reported = new List<PublishResult>();
        var publisher = new FakePublisher((c, _, _) => Created(c));

        await new ExportOrchestrator(publisher).RunAsync(
            [Candidate("A a"), Candidate("B b")], new PublishOptions(), new SynchronousProgress(reported.Add));

        Assert.Equal(2, reported.Count);
    }

    private sealed class SynchronousProgress(Action<PublishResult> onReport) : IProgress<PublishResult>
    {
        public void Report(PublishResult value) => onReport(value);
    }
}
