using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static INaturalistValidator.Rejection? Validate(ObservationCandidate? candidate = null, EvidenceFiles? evidence = null, INaturalistOptions? options = null) =>
        INaturalistValidator.Validate(candidate ?? TestData.Candidate(), evidence ?? TestData.Evidence(), options ?? TestData.Options(), Now);

    private static DateTimeOffset At(int year, int month, int day)
    {
        var local = new DateTime(year, month, day, 22, 0, 0);
        return new(local, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin").GetUtcOffset(local));
    }

    [Fact]
    public void Validate_ValidCandidate_Passes()
    {
        Assert.Null(Validate());
    }

    [Fact]
    public void Validate_PlatformNeutralChecks_RunToo()
    {
        // A host may call the publisher without the orchestrator; Core's checks must still apply.
        var result = Validate(TestData.Candidate() with { ObservedAt = Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(2)), Latitude = 0, Longitude = 0 });

        Assert.Equal(PublishStatus.SkippedInvalidEntry, result?.Status);
        Assert.Contains("in the future", result?.Message);
        Assert.Contains("GPS", result?.Message);
    }

    [Fact]
    public void Validate_EvidencePaths_AreNotJudged()
    {
        // The publisher gets the evidence already loaded; the relative test paths do not matter.
        Assert.Null(Validate(TestData.Candidate(spectrogram: "relative.png", audio: "relative.wav")));
    }

    [Theory]
    [InlineData(1896, 10, 4, false)]
    [InlineData(1896, 10, 3, true)]
    public void Validate_DateOlderThan130Years_IsRejected(int year, int month, int day, bool rejected)
    {
        var result = Validate(TestData.Candidate() with { ObservedAt = At(year, month, day) });

        Assert.Equal(rejected, result is not null);
        if (result is { } r)
        {
            Assert.Contains("130 years", r.Message);
        }
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    public void Validate_LatitudeAtAPole_IsRejected(double latitude)
    {
        var result = Validate(TestData.Candidate() with { Latitude = latitude });

        Assert.Equal(PublishStatus.SkippedInvalidEntry, result?.Status);
        Assert.Contains("iNaturalist would reject", result?.Message);
        Assert.Contains("Latitude", result?.Message);
    }

    [Fact]
    public void Validate_OneZeroCoordinate_Passes()
    {
        Assert.Null(Validate(TestData.Candidate() with { Latitude = 0, Longitude = 8.5 }));
    }

    [Theory]
    [InlineData(255, false)]
    [InlineData(256, true)]
    public void Validate_SpeciesNameLength_IsLimitedTo255(int length, bool rejected)
    {
        var result = Validate(TestData.Candidate() with { ScientificName = new string('a', length) });

        Assert.Equal(rejected, result is not null);
        if (result is { } r)
        {
            Assert.Contains("species name", r.Message);
        }
    }

    [Fact]
    public void Validate_TagListOver750Characters_IsRejected()
    {
        var tags = string.Join(",", Enumerable.Repeat(new string('t', 99), 8)); // 8 x 99 + 7 x 2 = 806 characters
        var result = Validate(options: new INaturalistOptions { ClientId = "c", TagList = tags });

        Assert.Equal(PublishStatus.SkippedInvalidEntry, result?.Status);
        Assert.Contains("TagList", result?.Message);
    }

    [Fact]
    public void Validate_TagLongerThan255Characters_IsRejected()
    {
        var result = Validate(options: new INaturalistOptions { ClientId = "c", TagList = "bat," + new string('t', 256) });

        Assert.Equal(PublishStatus.SkippedInvalidEntry, result?.Status);
        Assert.Contains("tag longer", result?.Message);
    }

    [Fact]
    public void Validate_EmptyTagList_Passes()
    {
        Assert.Null(Validate(options: new INaturalistOptions { ClientId = "c", TagList = "" }));
    }

    [Fact]
    public void Validate_EvidenceAtTheLimit_Passes()
    {
        var evidence = new EvidenceFiles(new EvidenceFile("s.png", new byte[100]), new EvidenceFile("a.wav", new byte[100]));

        Assert.Null(Validate(evidence: evidence, options: new INaturalistOptions { ClientId = "c", MaxEvidenceBytes = 100 }));
    }

    [Fact]
    public void Validate_OversizedAudio_IsInvalidEvidenceAndNamesFileSizeAndLimit()
    {
        var evidence = new EvidenceFiles(new EvidenceFile("s.png", new byte[10]), new EvidenceFile("night.wav", new byte[24_300_000]));

        var result = Validate(evidence: evidence);

        Assert.Equal(PublishStatus.SkippedInvalidEvidence, result?.Status);
        Assert.Contains("'night.wav'", result?.Message);
        Assert.Contains("24.3 MB", result?.Message);
        Assert.Contains("20 MB", result?.Message);
        Assert.DoesNotContain("s.png", result?.Message);
    }

    [Fact]
    public void Validate_BothFilesOversized_ReportsBoth()
    {
        var options = new INaturalistOptions { ClientId = "c", MaxEvidenceBytes = 5 };

        var result = Validate(options: options);

        Assert.Contains("spectrogram", result?.Message);
        Assert.Contains("audio", result?.Message);
    }

    [Fact]
    public void Validate_EntryProblemAndOversizedEvidence_ReportsTheEntryProblemFirst()
    {
        var evidence = new EvidenceFiles(new EvidenceFile("s.png", new byte[10]), new EvidenceFile("a.wav", new byte[30_000_000]));

        var result = Validate(TestData.Candidate() with { Latitude = 0, Longitude = 0 }, evidence);

        Assert.Equal(PublishStatus.SkippedInvalidEntry, result?.Status);
    }

    [Fact]
    public void Validate_SeveralEntryProblems_AreAllListed()
    {
        var result = Validate(TestData.Candidate() with { ObservedAt = At(1890, 1, 1), Latitude = 90 });

        Assert.Contains("130 years", result?.Message);
        Assert.Contains("Latitude", result?.Message);
    }
}
