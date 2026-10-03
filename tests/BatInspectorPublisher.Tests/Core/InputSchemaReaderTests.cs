using System.Globalization;
using BatInspectorPublisher.Core.InputSchema;

namespace BatInspectorPublisher.Tests.Core;

public class InputSchemaReaderTests
{
    private static string Entry(string? date = "13.06.2026 04:26:34", string lat = "49.8", string lon = "8.7", string species = "\"Pipistrellus nathusii\"",
        string png = "\"/data/a.png\"", string wav = "\"/data/a.wav\"", string extra = "") =>
        $$"""
        { "Date": {{(date is null ? "null" : $"\"{date}\"")}}, "Latitude": {{lat}}, "Longitude": {{lon}},
          "SpeciesLatin": {{species}}, "PathToPng": {{png}}, "PathToWav": {{wav}} {{extra}} }
        """;

    private static string Doc(string entries, string version = "\"SchemaVersion\": 1,") =>
        $$"""{ {{version}} "DocumentFiles": [ {{entries}} ] }""";

    /// <summary>The issues of the single rejected entry; fails if the entry was accepted.</summary>
    private static IReadOnlyList<ValidationIssue> RejectedIssues(string json)
    {
        var doc = InputSchemaReader.Parse(json);
        Assert.Empty(doc.Candidates);
        return Assert.Single(doc.Rejected).Issues;
    }

    private static string Json(string value) => "\"" + value.Replace("\\", "\\\\") + "\"";

    [Fact]
    public void ReadFile_SanitizedSample_ParsesAllEntries()
    {
        var doc = InputSchemaReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_v1.json"));

        Assert.Equal(1, doc.SchemaVersion);
        Assert.Equal(3, doc.Candidates.Count);
        Assert.Empty(doc.Rejected);

        var first = doc.Candidates[0];
        Assert.Equal("Pipistrellus nathusii", first.ScientificName);
        Assert.Equal("Rauhautfledermaus", first.LocalName);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 4, 26, 34, TimeSpan.FromHours(2)), first.ObservedAt);
        Assert.Equal(TimeSpan.FromHours(2), first.ObservedAt.Offset);
        Assert.Equal(50.11, first.Latitude);
        Assert.Equal(8.682, first.Longitude);
        Assert.Equal(20.052185, first.TemperatureCelsius);
        Assert.Equal(92.443848, first.HumidityPercent);
        Assert.Null(first.Comment);
        Assert.EndsWith("pnat_20260613_042634.png", first.SpectrogramPath);
        Assert.EndsWith("PNAT_20260613_042634.wav", first.AudioPath);

        Assert.Equal("unsicher, Verwechslung mit Mausohr-Arten möglich", doc.Candidates[2].Comment);
    }

    [Fact]
    public void Parse_WrongSpeciesCapitalization_IsNormalized()
    {
        var doc = InputSchemaReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_v1.json"));

        Assert.Equal("Eptesicus serotinus", doc.Candidates[1].ScientificName);
    }

    [Theory]
    [InlineData("  nyctalus   NOCTULA ", "Nyctalus noctula")]
    [InlineData("MYOTIS", "Myotis")]
    public void Parse_SpeciesWhitespaceAndCase_AreNormalized(string input, string expected)
    {
        var doc = InputSchemaReader.Parse(Doc(Entry(species: $"\"{input}\"")));

        Assert.Equal(expected, doc.Candidates.Single().ScientificName);
    }

    [Fact]
    public void Parse_MissingSchemaVersion_IsRejectedWithHint()
    {
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(), version: "")));

        var issue = Assert.Single(ex.Issues);
        Assert.Equal("SchemaVersion", issue.Path);
        Assert.Contains("SchemaVersion", issue.Message);
    }

    [Fact]
    public void Parse_NewerSchemaVersion_IsRejected()
    {
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(), version: "\"SchemaVersion\": 2,")));

        Assert.Contains("newer", ex.Issues.Single().Message);
    }

    [Theory]
    [InlineData("\"SchemaVersion\": 0,")]
    [InlineData("\"SchemaVersion\": \"1\",")]
    [InlineData("\"SchemaVersion\": 1.5,")]
    public void Parse_InvalidSchemaVersion_IsRejected(string version)
    {
        Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(), version)));
    }

    [Theory]
    [InlineData("{ nope")]
    [InlineData("[]")]
    [InlineData("""{ "DocumentFiles": [] }""")]
    [InlineData("""{ "SchemaVersion": 2, "DocumentFiles": [] }""")]
    [InlineData("""{ "SchemaVersion": 1 }""")]
    [InlineData("""{ "SchemaVersion": 1, "DocumentFiles": {} }""")]
    public void Parse_StructuralProblems_RejectTheWholeFile(string json)
    {
        Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(json));
    }

    [Fact]
    public void Parse_MissingDocumentFiles_ReportsThePath()
    {
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse("""{ "SchemaVersion": 1 }"""));

        Assert.Equal("DocumentFiles", ex.Issues.Single().Path);
    }

    [Fact]
    public void Parse_EmptyDocumentFiles_IsValid()
    {
        var doc = InputSchemaReader.Parse(Doc(""));

        Assert.Empty(doc.Candidates);
        Assert.Empty(doc.Rejected);
    }

    [Fact]
    public void Parse_UnknownProperties_AreIgnored()
    {
        var json = """{ "SchemaVersion": 1, "Future": 1, "DocumentFiles": [] }""";

        Assert.Empty(InputSchemaReader.Parse(json).Candidates);

        var withExtra = Doc(Entry(extra: ", \"SomethingNew\": { \"a\": 1 }"));
        Assert.Single(InputSchemaReader.Parse(withExtra).Candidates);
    }

    [Theory]
    [InlineData("2026-06-13 04:26:34")]
    [InlineData("13.06.2026")]
    [InlineData("32.06.2026 04:26:34")]
    public void Parse_BadDate_ReportsPathAndFormat(string date)
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(date: date))));

        Assert.Equal("DocumentFiles[0].Date", issue.Path);
        Assert.Contains("dd.MM.yyyy HH:mm:ss", issue.Message);
    }

    [Theory]
    [InlineData("91", "8.7", "Latitude")]
    [InlineData("-91", "8.7", "Latitude")]
    [InlineData("49.8", "181", "Longitude")]
    public void Parse_CoordinatesOutOfRange_AreRejected(string lat, string lon, string expectedField)
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(lat: lat, lon: lon))));

        Assert.Equal($"DocumentFiles[0].{expectedField}", issue.Path);
    }

    [Fact]
    public void Parse_MissingRequiredFields_AreAllReportedWithIndex()
    {
        var json = Doc(Entry() + "," + """{ "Date": "13.06.2026 04:26:34", "Latitude": 49.8 }""");

        var doc = InputSchemaReader.Parse(json);

        Assert.Single(doc.Candidates);
        var rejected = Assert.Single(doc.Rejected);
        Assert.Equal(1, rejected.Index);
        var paths = rejected.Issues.Select(i => i.Path).ToList();
        Assert.Contains("DocumentFiles[1].Longitude", paths);
        Assert.Contains("DocumentFiles[1].SpeciesLatin", paths);
        Assert.Contains("DocumentFiles[1].PathToPng", paths);
        Assert.Contains("DocumentFiles[1].PathToWav", paths);
    }

    [Fact]
    public void Parse_WrongTypes_AreReported()
    {
        var paths = RejectedIssues(Doc(Entry(lat: "\"north\"", extra: ", \"Temperature\": \"warm\""))).Select(i => i.Path).ToList();

        Assert.Contains("DocumentFiles[0].Latitude", paths);
        Assert.Contains("DocumentFiles[0].Temperature", paths);
    }

    [Fact]
    public void Parse_EmptyOptionalStrings_BecomeNull()
    {
        var doc = InputSchemaReader.Parse(Doc(Entry(extra: ", \"Comment\": \"  \", \"SpeciesLocal\": \"\"")));

        var candidate = doc.Candidates.Single();
        Assert.Null(candidate.Comment);
        Assert.Null(candidate.LocalName);
    }

    [Fact]
    public void Parse_EmptyRequiredString_IsRejected()
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(species: "\"  \""))));

        Assert.Equal("DocumentFiles[0].SpeciesLatin", issue.Path);
    }

    [Fact]
    public void Read_Stream_Works()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_v1.json"));

        Assert.Equal(3, InputSchemaReader.Read(stream).Candidates.Count);
    }

    [Fact]
    public void Parse_DoesNotRequireEvidenceFilesToExist()
    {
        var doc = InputSchemaReader.Parse(Doc(Entry(png: "\"F:\\\\does\\\\not\\\\exist.png\"")));

        Assert.Single(doc.Candidates);
    }

    [Theory]
    [InlineData("/data/a.png")]
    [InlineData("C:\\data\\a.png")]
    [InlineData("c:/data/a.PNG")]
    [InlineData("\\\\server\\share\\a.png")]
    public void Parse_AbsolutePathsOfEitherOsSyntax_AreAccepted(string png)
    {
        Assert.Single(InputSchemaReader.Parse(Doc(Entry(png: Json(png)))).Candidates);
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("data/a.png")]
    [InlineData("..\\a.png")]
    [InlineData("C:a.png")]
    [InlineData("~/a.png")]
    public void Parse_RelativeEvidencePath_IsRejected(string png)
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(png: Json(png)))));

        Assert.Equal("DocumentFiles[0].PathToPng", issue.Path);
        Assert.Contains("absolute", issue.Message);
    }

    [Theory]
    [InlineData("/data/a.jpg", "/data/a.wav", "PathToPng")]
    [InlineData("/data/a.png", "/data/a.mp3", "PathToWav")]
    [InlineData("/data/a.png", "/data/.wav", "PathToWav")]
    [InlineData("/data/a.png/secret", "/data/a.wav", "PathToPng")]
    [InlineData("/data/a.wav", "/data/a.png", "PathToPng")]
    public void Parse_WrongEvidenceExtension_IsRejected(string png, string wav, string expectedField)
    {
        var issues = RejectedIssues(Doc(Entry(png: Json(png), wav: Json(wav))));

        Assert.Contains(issues, i => i.Path == $"DocumentFiles[0].{expectedField}");
    }

    [Fact]
    public void Parse_LatitudeAndLongitudeBothZero_IsRejectedAsMissingGps()
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(lat: "0", lon: "0.0"))));

        Assert.Contains("GPS", issue.Message);
    }

    [Theory]
    [InlineData("0", "8.7")]
    [InlineData("49.8", "0")]
    public void Parse_OneCoordinateZero_IsValid(string lat, string lon)
    {
        Assert.Single(InputSchemaReader.Parse(Doc(Entry(lat: lat, lon: lon))).Candidates);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 13.06.2026 04:26:34 Berlin time (CEST, UTC+2) is 02:26:34 UTC.
    [Theory]
    [InlineData("2026-06-13T02:26:34Z", true)]
    [InlineData("2026-06-13T02:26:35Z", true)]
    [InlineData("2026-06-13T02:26:33Z", false)]
    [InlineData("2026-06-13T01:00:00Z", false)]
    public void Parse_DateInTheFuture_IsComparedWithBerlinTime(string nowUtc, bool valid)
    {
        var time = new FixedTime(DateTimeOffset.Parse(nowUtc, CultureInfo.InvariantCulture));

        var doc = InputSchemaReader.Parse(Doc(Entry()), time);

        if (valid)
        {
            Assert.Single(doc.Candidates);
        }
        else
        {
            Assert.Empty(doc.Candidates);
            Assert.Equal("DocumentFiles[0].Date", Assert.Single(Assert.Single(doc.Rejected).Issues).Path);
        }
    }

    [Theory]
    [InlineData("15.01.2026 12:00:00", 1)]
    [InlineData("15.07.2026 12:00:00", 2)]
    public void Parse_Date_GetsTheBerlinOffsetOfThatDate(string date, int offsetHours)
    {
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry(date)), new FixedTime(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture))).Candidates);

        Assert.Equal(TimeSpan.FromHours(offsetHours), candidate.ObservedAt.Offset);
        Assert.Equal(12, candidate.ObservedAt.Hour);
    }

    [Fact]
    public void Parse_TimeInSpringForwardGap_IsRejected()
    {
        // 29.03.2026: 02:00 jumps to 03:00 in Berlin, so 02:30 never existed.
        var issue = Assert.Single(RejectedIssues(Doc(Entry("29.03.2026 02:30:00"))));

        Assert.Equal("DocumentFiles[0].Date", issue.Path);
        Assert.Contains("does not exist", issue.Message);
    }

    [Theory]
    [InlineData("29.03.2026 01:59:59", 1)]
    [InlineData("29.03.2026 03:00:00", 2)]
    public void Parse_TimesAroundSpringForwardGap_AreValid(string date, int offsetHours)
    {
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry(date)), new FixedTime(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture))).Candidates);

        Assert.Equal(TimeSpan.FromHours(offsetHours), candidate.ObservedAt.Offset);
    }

    [Theory]
    [InlineData("01.01.0001 00:00:00", "Europe/Berlin")]
    [InlineData("01.01.0001 00:30:00", "Asia/Tokyo")]
    [InlineData("31.12.9999 23:59:59", "America/New_York")]
    public void Parse_DateAtTheEdgeOfTheSupportedRange_IsRejectedNotThrown(string date, string zone)
    {
        var doc = InputSchemaReader.Parse(Doc(Entry(date, extra: $$""", "TimeZone": "{{zone}}" """)), Later);

        Assert.Empty(doc.Candidates);
        Assert.Equal("DocumentFiles[0].Date", Assert.Single(Assert.Single(doc.Rejected).Issues).Path);
    }

    [Fact]
    public void Parse_TimeInRepeatedAutumnHour_IsReadAsStandardTime()
    {
        // 25.10.2026: 03:00 CEST falls back to 02:00 CET, so 02:30 happens twice. The standard-time reading is documented.
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry("25.10.2026 02:30:00")), new FixedTime(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture))).Candidates);

        Assert.Equal(TimeSpan.FromHours(1), candidate.ObservedAt.Offset);
    }

    [Fact]
    public void Parse_NightAcrossMidnight_UsesTheBerlinDateNotTheUtcDate()
    {
        // 22:30 UTC is 00:30 Berlin (CEST) on the next day.
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry("14.06.2026 00:30:00"))).Candidates);

        Assert.Equal(new DateTime(2026, 6, 14), candidate.ObservedAt.Date);
        Assert.Equal(new DateTime(2026, 6, 13, 22, 30, 0), candidate.ObservedAt.UtcDateTime);
    }

    [Theory]
    [InlineData("13.06.2026 04:26:34Z")]
    [InlineData("13.06.2026 04:26:34 +02:00")]
    [InlineData("2026-06-13T04:26:34+02:00")]
    public void Parse_DateWithZone_IsRejectedAsFormatError(string date)
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(date))));

        Assert.Equal("DocumentFiles[0].Date", issue.Path);
        Assert.Contains("format", issue.Message);
    }

    private static readonly FixedTime Later = new(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture));

    [Fact]
    public void Parse_TimeZoneField_IsUsedInsteadOfBerlin()
    {
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry("15.07.2026 23:30:00", extra: """, "TimeZone": "Europe/Lisbon" """)), Later).Candidates);

        Assert.Equal(TimeSpan.FromHours(1), candidate.ObservedAt.Offset);
        Assert.Equal("Europe/Lisbon", candidate.TimeZoneId);
        Assert.Equal(new DateTime(2026, 7, 15, 22, 30, 0), candidate.ObservedAt.UtcDateTime);
    }

    [Fact]
    public void Parse_WithoutTimeZoneField_IsBerlin()
    {
        var candidate = Assert.Single(InputSchemaReader.Parse(Doc(Entry()), Later).Candidates);

        Assert.Equal("Europe/Berlin", candidate.TimeZoneId);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("+02:00")]
    [InlineData("../etc/passwd")]
    public void Parse_UnknownTimeZone_IsRejected(string zone)
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(extra: $$""", "TimeZone": "{{zone}}" """))));

        Assert.Equal("DocumentFiles[0].TimeZone", issue.Path);
    }

    [Fact]
    public void Parse_TimeZoneNotAString_IsRejected()
    {
        var issue = Assert.Single(RejectedIssues(Doc(Entry(extra: ", \"TimeZone\": 2"))));

        Assert.Equal("DocumentFiles[0].TimeZone", issue.Path);
    }

    [Fact]
    public void Parse_GapAndFutureAreChecked_InTheNamedZone()
    {
        // Lisbon changes the clocks on 29.03.2026 at 01:00, so 01:30 does not exist there (it does in Berlin as CET).
        var gap = Assert.Single(RejectedIssues(Doc(Entry("29.03.2026 01:30:00", extra: """, "TimeZone": "Europe/Lisbon" """))));
        Assert.Contains("does not exist", gap.Message);

        // 12:30 in Auckland (NZDT, UTC+13) on 01.01.2027 is 23:30 UTC on 31.12., after 23:00 UTC.
        var future = new FixedTime(DateTimeOffset.Parse("2026-12-31T23:00:00Z", CultureInfo.InvariantCulture));
        var doc = InputSchemaReader.Parse(Doc(Entry("01.01.2027 12:30:00", extra: """, "TimeZone": "Pacific/Auckland" """)), future);
        Assert.Empty(doc.Candidates);
    }

    [Fact]
    public void Parse_AmbiguousAutumnHour_AcceptedWithWarning()
    {
        var doc = InputSchemaReader.Parse(Doc(Entry("25.10.2026 02:30:00") + "," + Entry("25.10.2026 04:00:00")), Later);

        Assert.Equal(2, doc.Candidates.Count);
        var warning = Assert.Single(doc.Warnings);
        Assert.Equal(0, warning.Index);
        Assert.Equal("DocumentFiles[0].Date", warning.Issue.Path);
    }

    [Fact]
    public void Parse_RejectedEntry_HasNoWarning()
    {
        var doc = InputSchemaReader.Parse(Doc(Entry("25.10.2026 02:30:00", lat: "0", lon: "0")), Later);

        Assert.Empty(doc.Warnings);
        Assert.Single(doc.Rejected);
    }

    [Fact]
    public void Parse_BadEntriesAreLeftOutAndReported_ValidOnesKept()
    {
        var json = Doc(string.Join(",",
            Entry(species: "\"Good good\""),
            Entry(lat: "0", lon: "0", png: "\"rel.png\""),
            "42",
            Entry(species: "\"Also good\"")));

        var doc = InputSchemaReader.Parse(json);

        Assert.Equal(["Good good", "Also good"], doc.Candidates.Select(c => c.ScientificName));
        Assert.Equal([1, 2], doc.Rejected.Select(r => r.Index));
        Assert.Equal(2, doc.Rejected[0].Issues.Count);
        Assert.All(doc.Rejected[0].Issues, i => Assert.StartsWith("DocumentFiles[1].", i.Path));
        Assert.Equal("DocumentFiles[2]", doc.Rejected[1].Issues.Single().Path);
    }
}
