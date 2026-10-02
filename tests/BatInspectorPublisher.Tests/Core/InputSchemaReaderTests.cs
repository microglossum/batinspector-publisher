using BatInspectorPublisher.Core.InputSchema;

namespace BatInspectorPublisher.Tests.Core;

public class InputSchemaReaderTests
{
    private static string Entry(string? date = "13.06.2026 04:26:34", string lat = "49.8", string lon = "8.7", string species = "\"Pipistrellus nathusii\"",
        string png = "\"a.png\"", string wav = "\"a.wav\"", string extra = "") =>
        $$"""
        { "Date": {{(date is null ? "null" : $"\"{date}\"")}}, "Latitude": {{lat}}, "Longitude": {{lon}},
          "SpeciesLatin": {{species}}, "PathToPng": {{png}}, "PathToWav": {{wav}} {{extra}} }
        """;

    private static string Doc(string entries, string version = "\"SchemaVersion\": 1,") =>
        $$"""{ {{version}} "DocumentFiles": [ {{entries}} ] }""";

    [Fact]
    public void ReadFile_SanitizedSample_ParsesAllEntries()
    {
        var doc = InputSchemaReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample_v1.json"));

        Assert.Equal(1, doc.SchemaVersion);
        Assert.Equal(3, doc.Candidates.Count);

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

    [Fact]
    public void Parse_NotJson_ThrowsInputSchemaException()
    {
        Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse("{ nope"));
    }

    [Fact]
    public void Parse_RootNotObject_Throws()
    {
        Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse("[]"));
    }

    [Fact]
    public void Parse_MissingDocumentFiles_Throws()
    {
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse("""{ "SchemaVersion": 1 }"""));

        Assert.Equal("DocumentFiles", ex.Issues.Single().Path);
    }

    [Fact]
    public void Parse_EmptyDocumentFiles_IsValid()
    {
        Assert.Empty(InputSchemaReader.Parse(Doc("")).Candidates);
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
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(), "\"SchemaVersion\": 1,").Replace("13.06.2026 04:26:34", date)));

        var issue = Assert.Single(ex.Issues);
        Assert.Equal("DocumentFiles[0].Date", issue.Path);
        Assert.Contains("dd.MM.yyyy HH:mm:ss", issue.Message);
    }

    [Theory]
    [InlineData("91", "8.7", "Latitude")]
    [InlineData("-91", "8.7", "Latitude")]
    [InlineData("49.8", "181", "Longitude")]
    public void Parse_CoordinatesOutOfRange_AreRejected(string lat, string lon, string expectedField)
    {
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(lat: lat, lon: lon))));

        Assert.Equal($"DocumentFiles[0].{expectedField}", ex.Issues.Single().Path);
    }

    [Fact]
    public void Parse_MissingRequiredFields_AreAllReportedWithIndex()
    {
        var json = Doc(Entry() + "," + """{ "Date": "13.06.2026 04:26:34", "Latitude": 49.8 }""");

        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(json));

        var paths = ex.Issues.Select(i => i.Path).ToList();
        Assert.Contains("DocumentFiles[1].Longitude", paths);
        Assert.Contains("DocumentFiles[1].SpeciesLatin", paths);
        Assert.Contains("DocumentFiles[1].PathToPng", paths);
        Assert.Contains("DocumentFiles[1].PathToWav", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("DocumentFiles[0]"));
    }

    [Fact]
    public void Parse_WrongTypes_AreReported()
    {
        var ex = Assert.Throws<InputSchemaException>(() =>
            InputSchemaReader.Parse(Doc(Entry(lat: "\"north\"", extra: ", \"Temperature\": \"warm\""))));

        var paths = ex.Issues.Select(i => i.Path).ToList();
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
        var ex = Assert.Throws<InputSchemaException>(() => InputSchemaReader.Parse(Doc(Entry(species: "\"  \""))));

        Assert.Equal("DocumentFiles[0].SpeciesLatin", ex.Issues.Single().Path);
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
}
