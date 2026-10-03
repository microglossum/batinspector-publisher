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
        Assert.Equal(new DateTime(2026, 6, 13, 4, 26, 34), first.ObservedAt);
        Assert.Equal(DateTimeKind.Unspecified, first.ObservedAt.Kind);
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
