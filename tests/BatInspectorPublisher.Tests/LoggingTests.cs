using System.Net;
using System.Reflection;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;
using BatInspectorPublisher.Tests.Adapters.INaturalist;
using Microsoft.Extensions.Logging;

namespace BatInspectorPublisher.Tests;

/// <summary>What the library logs is a contract for hosts: stable event IDs, a level policy and no secrets.</summary>
public class LoggingTests
{
    private static List<LoggerMessageAttribute> EventDefinitions() => typeof(ExportOrchestrator).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
        .OfType<LoggerMessageAttribute>()
        .ToList();

    [Fact]
    public void EventIds_AreUniqueAndNamed()
    {
        var definitions = EventDefinitions();

        Assert.NotEmpty(definitions);
        Assert.Equal(definitions.Count, definitions.Select(d => d.EventId).Distinct().Count());
        Assert.All(definitions, d => Assert.False(string.IsNullOrWhiteSpace(d.EventName)));
    }

    private sealed class ScriptedPublisher(Func<ObservationCandidate, PublishResult> publish) : IObservationPublisher
    {
        public string PlatformId => "fake";

        public Task<PublishResult> PublishAsync(ObservationCandidate candidate, EvidenceFiles evidence, PublishOptions options, CancellationToken ct = default) =>
            Task.FromResult(publish(candidate));
    }

    private static (ObservationCandidate Candidate, string Dir) CandidateWithEvidence()
    {
        var dir = Directory.CreateTempSubdirectory("logging-test-").FullName;
        var png = Path.Combine(dir, "a.png");
        var wav = Path.Combine(dir, "a.wav");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        File.WriteAllBytes(wav, [.. "RIFF"u8, 4, 0, 0, 0, .. "WAVE"u8, 1, 2]);
        return (TestData.Candidate() with { SpectrogramPath = png, AudioPath = wav }, dir);
    }

    [Fact]
    public async Task Orchestrator_FailedCandidate_IsLoggedAtErrorWithItsExceptionAndMessage()
    {
        var (candidate, dir) = CandidateWithEvidence();
        try
        {
            var logger = new CapturingLogger();
            var boom = new InvalidOperationException("boom");
            var publisher = new ScriptedPublisher(c => new PublishResult
            {
                Candidate = c,
                PlatformId = "fake",
                Status = PublishStatus.Failed,
                Message = "boom (observation 7 incomplete)",
                Error = boom,
            });

            await new ExportOrchestrator(publisher, new CapturingLogger<ExportOrchestrator>(logger)).RunAsync([candidate], new PublishOptions { Commit = true });

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(boom, entry.Error);
            Assert.Contains("observation 7 incomplete", entry.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Orchestrator_SkippedCandidate_IsLoggedAtInformationWithStatusAndReason()
    {
        var (candidate, dir) = CandidateWithEvidence();
        try
        {
            var logger = new CapturingLogger();
            var publisher = new ScriptedPublisher(c => new PublishResult
            {
                Candidate = c,
                PlatformId = "fake",
                Status = PublishStatus.SkippedDuplicate,
                Message = "already there",
            });

            await new ExportOrchestrator(publisher, new CapturingLogger<ExportOrchestrator>(logger)).RunAsync([candidate], new PublishOptions());

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Contains("SkippedDuplicate", entry.Message);
            Assert.Contains("already there", entry.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Login_NeverLogsATokenAtAnyLevel()
    {
        var http = new StubHttpHandler()
            .On("POST /oauth/token", HttpStatusCode.OK, """{ "access_token": "oauth-secret-1", "refresh_token": "refresh-secret-2" }""")
            .On("GET /users/api_token", HttpStatusCode.OK, """{ "api_token": "jwt-secret-3" }""")
            .On("GET /v1/users/me", HttpStatusCode.OK, """{ "results": [ { "login": "bat-fan" } ] }""");
        var logger = new CapturingLogger();
        var redirect = $"http://127.0.0.1:{TestData.FreeTcpPort()}/callback";
        var auth = new INaturalistAuthenticator(TestData.Options(redirect), new HttpClient(http), new InMemoryTokenStore(), (url, ct) =>
        {
            var state = System.Web.HttpUtility.ParseQueryString(url.Query)["state"];
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                using var client = new HttpClient();
                await client.GetAsync($"{redirect}?code=c&state={state}");
            });
            return Task.CompletedTask;
        }, new CapturingLogger<INaturalistAuthenticator>(logger));

        await auth.EnsureAuthenticatedAsync();

        Assert.NotEmpty(logger.Entries);
        var everything = string.Join('\n', logger.Entries.Select(e => e.Message + e.Error));
        Assert.DoesNotContain("oauth-secret-1", everything);
        Assert.DoesNotContain("refresh-secret-2", everything);
        Assert.DoesNotContain("jwt-secret-3", everything);
    }
}
