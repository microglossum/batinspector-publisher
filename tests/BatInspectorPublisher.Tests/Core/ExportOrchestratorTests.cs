using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Core;

public class ExportOrchestratorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("orchestrator-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ObservationCandidate Candidate(string species, bool withEvidence = true)
    {
        var png = Path.Combine(_dir, species + ".png");
        var wav = Path.Combine(_dir, species + ".wav");
        if (withEvidence)
        {
            File.WriteAllText(png, "png");
            File.WriteAllText(wav, "wav");
        }

        return new ObservationCandidate
        {
            ScientificName = species,
            ObservedAt = new DateTime(2026, 6, 13, 4, 0, 0),
            Latitude = 50,
            Longitude = 8,
            SpectrogramPath = png,
            AudioPath = wav,
        };
    }

    private sealed class FakePublisher(Func<ObservationCandidate, PublishOptions, CancellationToken, PublishResult> publish) : IObservationPublisher
    {
        public List<(ObservationCandidate Candidate, PublishOptions Options)> Calls { get; } = [];
        public string PlatformId => "fake";

        public Task<PublishResult> PublishAsync(ObservationCandidate candidate, PublishOptions options, CancellationToken ct = default)
        {
            Calls.Add((candidate, options));
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
    public async Task RunAsync_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        var publisher = new FakePublisher((c, _, _) =>
        {
            cts.Cancel();
            return Created(c);
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ExportOrchestrator(publisher).RunAsync([Candidate("A a"), Candidate("B b")], new PublishOptions { Commit = true }, ct: cts.Token));

        Assert.Single(publisher.Calls);
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
