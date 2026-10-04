using BatInspectorPublisher.Core.InputSchema;
using BatInspectorPublisher.Core.Validation;
using BatInspectorPublisher.Tests.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Core;

public class EntryValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static EntryValues Valid() => new(
        new DateTimeOffset(2026, 6, 11, 21, 37, 49, TimeSpan.FromHours(2)), 50.11, 8.682, "/data/a.png", "/data/a.wav");

    private static List<string> Paths(EntryValues values) => EntryValidator.ValidateValues(values, Now).Select(i => i.Path).ToList();

    [Fact]
    public void ValidateValues_ValidEntry_HasNoIssues()
    {
        Assert.Empty(EntryValidator.ValidateValues(Valid(), Now));
    }

    [Fact]
    public void ValidateValues_ACandidateBuiltInCode_IsJudgedLikeOneFromAFile()
    {
        var candidate = TestData.Candidate() with { SpectrogramPath = "relative/a.png", Latitude = 0, Longitude = 0 };

        var issues = EntryValidator.ValidateValues(EntryValues.From(candidate), Now);

        Assert.Contains(issues, i => i.Path == "SpectrogramPath" && i.Message.Contains("absolute"));
        Assert.Contains(issues, i => i.Path == "Latitude" && i.Message.Contains("GPS"));
    }

    [Theory]
    [InlineData(90, false)]
    [InlineData(-90, false)]
    [InlineData(90.1, true)]
    public void ValidateValues_LatitudeRange_IsClosedAt90(double latitude, bool rejected)
    {
        Assert.Equal(rejected, Paths(Valid() with { Latitude = latitude }).Contains("Latitude"));
    }

    [Theory]
    [InlineData(180, false)]
    [InlineData(-180.5, true)]
    public void ValidateValues_LongitudeRange_IsClosedAt180(double longitude, bool rejected)
    {
        Assert.Equal(rejected, Paths(Valid() with { Longitude = longitude }).Contains("Longitude"));
    }

    [Fact]
    public void ValidateValues_OnlyOneCoordinateZero_Passes()
    {
        Assert.Empty(Paths(Valid() with { Latitude = 0 }));
    }

    [Fact]
    public void ValidateValues_FutureInstant_IsRejectedButNowIsNot()
    {
        Assert.Contains("ObservedAt", Paths(Valid() with { ObservedAt = Now.AddSeconds(1) }));
        Assert.Empty(Paths(Valid() with { ObservedAt = Now }));
    }

    [Theory]
    [InlineData("/data/a.jpg", "/data/a.wav", "SpectrogramPath")]
    [InlineData("/data/a.png", "/data/a.mp3", "AudioPath")]
    [InlineData("/data/a.png", "/data/.wav", "AudioPath")]
    [InlineData("a.png", "/data/a.wav", "SpectrogramPath")]
    [InlineData(@"C:\data\a.png", @"\\server\share\a.wav", "")]
    public void ValidateValues_EvidencePaths_MustBeAbsoluteWithTheRightExtension(string png, string wav, string expectedField)
    {
        var paths = Paths(Valid() with { SpectrogramPath = png, AudioPath = wav });

        Assert.Equal(expectedField.Length == 0 ? [] : [expectedField], paths);
    }

    [Fact]
    public void ValidateValues_UnreadValuesAreSkippedAndEveryOtherProblemIsReported()
    {
        var paths = Paths(new EntryValues(null, 91, null, "a.png", null));

        Assert.Equal(["Latitude", "SpectrogramPath"], paths);
    }

    [Fact]
    public void ValidateEvidenceContent_Png_ChecksTheSignature()
    {
        Assert.Null(EntryValidator.ValidateEvidenceContent(EvidenceKind.Spectrogram, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("not a PNG image", EntryValidator.ValidateEvidenceContent(EvidenceKind.Spectrogram, "not a png"u8));
    }

    [Fact]
    public void ValidateEvidenceContent_Wav_NeedsRiffAndWave()
    {
        Assert.Null(EntryValidator.ValidateEvidenceContent(EvidenceKind.Audio, [.. "RIFF"u8, 4, 0, 0, 0, .. "WAVE"u8]));
        Assert.Equal("not a WAV recording", EntryValidator.ValidateEvidenceContent(EvidenceKind.Audio, [.. "RIFF"u8, 4, 0, 0, 0, .. "AVI "u8]));
        Assert.Equal("not a WAV recording", EntryValidator.ValidateEvidenceContent(EvidenceKind.Audio, "RIFF"u8));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidateEvidenceContent_EmptyFile_IsEmpty(bool audio)
    {
        Assert.Equal("empty", EntryValidator.ValidateEvidenceContent(audio ? EvidenceKind.Audio : EvidenceKind.Spectrogram, []));
    }

    [Theory]
    [InlineData(-40, false)]
    [InlineData(60, false)]
    [InlineData(60.5, true)]
    [InlineData(-127, true)]
    [InlineData(85, true)]
    public void PlausibleTemperature_OutsideTheRange_IsLeftOutWithAWarning(double value, bool dropped)
    {
        var warnings = new List<ValidationIssue>();

        var result = EntryValidator.PlausibleTemperature(value, warnings);

        Assert.Equal(dropped ? null : value, result);
        Assert.Equal(dropped, warnings.Any(w => w.Path == "TemperatureCelsius"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(-1, true)]
    [InlineData(101, true)]
    public void PlausibleHumidity_NotAPercentage_IsLeftOutWithAWarning(double value, bool dropped)
    {
        var warnings = new List<ValidationIssue>();

        var result = EntryValidator.PlausibleHumidity(value, warnings);

        Assert.Equal(dropped ? null : value, result);
        Assert.Equal(dropped, warnings.Any(w => w.Path == "HumidityPercent"));
    }

    [Fact]
    public void PlausibleMeasurements_Absent_StayAbsentWithoutWarning()
    {
        var warnings = new List<ValidationIssue>();

        Assert.Null(EntryValidator.PlausibleTemperature(null, warnings));
        Assert.Null(EntryValidator.PlausibleHumidity(null, warnings));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(23, false)]
    [InlineData(2, false)]
    public void ValidatePlausibility_DaylightHour_IsWarnedAboutInLocalTime(int hour, bool warned)
    {
        // The offset is part of the value: 12:00 UTC is 14:00 here, but the local hour counts.
        var at = new DateTimeOffset(2026, 6, 11, hour, 30, 0, TimeSpan.FromHours(2));

        var warnings = EntryValidator.ValidatePlausibility("Pipistrellus nathusii", at);

        Assert.Equal(warned, warnings.Any(w => w.Path == "ObservedAt"));
    }

    [Theory]
    [InlineData("Pipistrellus nathusii", false)]
    [InlineData("Pipistrellus", false)]
    [InlineData("?", false)]
    [InlineData("Myotis myotis x blythii", true)]
    [InlineData("Myotis myotis oxygnathus", true)]
    public void ValidatePlausibility_NameThatIsNotAGenusOrBinomial_IsWarnedAbout(string name, bool warned)
    {
        var warnings = EntryValidator.ValidatePlausibility(name, null);

        Assert.Equal(warned, warnings.Any(w => w.Path == "ScientificName"));
    }

    [Fact]
    public void DuplicateEntryFinder_SameSpeciesTimeAndPosition_WarnsOnTheLaterEntryOnly()
    {
        var finder = new DuplicateEntryFinder();
        var candidate = TestData.Candidate();

        Assert.Null(finder.Check(0, "DocumentFiles[0]", candidate));
        var warning = finder.Check(3, "DocumentFiles[3]", candidate with { Comment = "other" });

        Assert.NotNull(warning);
        Assert.Equal("DocumentFiles[3]", warning.Path);
        Assert.Contains("DocumentFiles[0]", warning.Message);
    }

    [Fact]
    public void DuplicateEntryFinder_AnyDifference_IsNoDuplicate()
    {
        var finder = new DuplicateEntryFinder();
        var candidate = TestData.Candidate();
        finder.Check(0, "DocumentFiles[0]", candidate);

        Assert.Null(finder.Check(1, "DocumentFiles[1]", candidate with { ScientificName = "Other species" }));
        Assert.Null(finder.Check(2, "DocumentFiles[2]", candidate with { ObservedAt = candidate.ObservedAt.AddSeconds(1) }));
        Assert.Null(finder.Check(3, "DocumentFiles[3]", candidate with { Latitude = candidate.Latitude + 0.001 }));
        Assert.Null(finder.Check(4, "DocumentFiles[4]", candidate with { Longitude = candidate.Longitude + 0.001 }));
    }
}
