using System.Globalization;
using System.Text.Json;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.InputSchema;

/// <summary>
/// Reads the BatInspector export file (<c>{ "SchemaVersion": 1, "DocumentFiles": [ ... ] }</c>).
/// Strict about required fields and the version, tolerant of unknown properties (additive changes
/// stay compatible within one schema version). Does not touch the file system for evidence files;
/// existence of the referenced spectrogram/audio is checked at publish time.
/// </summary>
public static class InputSchemaReader
{
    /// <summary>Highest schema version this reader understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Date format used by BatInspector, German local time without zone.</summary>
    internal const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    /// <summary>Reads and validates a file.</summary>
    /// <exception cref="InputSchemaException">The content is invalid.</exception>
    public static InputDocument ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads and validates a stream (UTF-8 JSON).</summary>
    /// <exception cref="InputSchemaException">The content is invalid.</exception>
    public static InputDocument Read(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses and validates a JSON string.</summary>
    /// <exception cref="InputSchemaException">The content is invalid.</exception>
    public static InputDocument Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InputSchemaException([new("$", $"Not valid JSON: {ex.Message}")], ex);
        }

        using (doc)
        {
            var issues = new List<ValidationIssue>();
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InputSchemaException([new("$", "Root must be a JSON object.")]);
            }

            var version = ReadVersion(root, issues);
            if (version is null)
            {
                throw new InputSchemaException(issues);
            }

            var candidates = ReadCandidates(root, issues);
            if (issues.Count > 0)
            {
                throw new InputSchemaException(issues);
            }

            return new InputDocument(version.Value, candidates);
        }
    }

    private static int? ReadVersion(JsonElement root, List<ValidationIssue> issues)
    {
        if (!root.TryGetProperty("SchemaVersion", out var el))
        {
            issues.Add(new("SchemaVersion", "Required. Add \"SchemaVersion\": 1 at the top level."));
            return null;
        }

        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var version) || version < 1)
        {
            issues.Add(new("SchemaVersion", "Must be a positive integer."));
            return null;
        }

        if (version > CurrentSchemaVersion)
        {
            issues.Add(new("SchemaVersion",
                $"Version {version} is newer than the highest supported version {CurrentSchemaVersion}. Update BatInspectorPublisher."));
            return null;
        }

        return version;
    }

    private static List<ObservationCandidate> ReadCandidates(JsonElement root, List<ValidationIssue> issues)
    {
        var candidates = new List<ObservationCandidate>();
        if (!root.TryGetProperty("DocumentFiles", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("DocumentFiles", "Required array."));
            return candidates;
        }

        var index = 0;
        foreach (var entry in files.EnumerateArray())
        {
            var path = $"DocumentFiles[{index++}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new(path, "Must be an object."));
                continue;
            }

            var before = issues.Count;
            var species = RequiredString(entry, "SpeciesLatin", path, issues);
            var observedAt = RequiredDate(entry, "Date", path, issues);
            var lat = RequiredCoordinate(entry, "Latitude", 90, path, issues);
            var lon = RequiredCoordinate(entry, "Longitude", 180, path, issues);
            var png = RequiredString(entry, "PathToPng", path, issues);
            var wav = RequiredString(entry, "PathToWav", path, issues);
            var temperature = OptionalNumber(entry, "Temperature", path, issues);
            var humidity = OptionalNumber(entry, "Humidity", path, issues);
            var local = OptionalString(entry, "SpeciesLocal", path, issues);
            var comment = OptionalString(entry, "Comment", path, issues);

            if (issues.Count > before)
            {
                continue;
            }

            candidates.Add(new ObservationCandidate
            {
                ScientificName = ScientificName.Normalize(species!),
                LocalName = local,
                ObservedAt = observedAt!.Value,
                Latitude = lat!.Value,
                Longitude = lon!.Value,
                TemperatureCelsius = temperature,
                HumidityPercent = humidity,
                Comment = comment,
                SpectrogramPath = png!,
                AudioPath = wav!,
            });
        }

        return candidates;
    }

    private static string? RequiredString(JsonElement obj, string name, string path, List<ValidationIssue> issues)
    {
        var value = OptionalString(obj, name, path, issues);
        if (value is null && !issues.Any(i => i.Path == $"{path}.{name}"))
        {
            issues.Add(new($"{path}.{name}", "Required."));
        }

        return value;
    }

    private static string? OptionalString(JsonElement obj, string name, string path, List<ValidationIssue> issues)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            issues.Add(new($"{path}.{name}", "Must be a string."));
            return null;
        }

        var value = el.GetString()!.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        return value;
    }

    private static DateTime? RequiredDate(JsonElement obj, string name, string path, List<ValidationIssue> issues)
    {
        var text = RequiredString(obj, name, path, issues);
        if (text is null)
        {
            return null;
        }

        if (!DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            issues.Add(new($"{path}.{name}", $"'{text}' does not match the format {DateFormat} (e.g. 13.06.2026 04:26:34)."));
            return null;
        }

        return date;
    }

    private static double? OptionalNumber(JsonElement obj, string name, string path, List<ValidationIssue> issues)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (el.ValueKind != JsonValueKind.Number)
        {
            issues.Add(new($"{path}.{name}", "Must be a number."));
            return null;
        }

        return el.GetDouble();
    }

    private static double? RequiredCoordinate(JsonElement obj, string name, double limit, string path, List<ValidationIssue> issues)
    {
        var value = OptionalNumber(obj, name, path, issues);
        if (value is null)
        {
            if (!issues.Any(i => i.Path == $"{path}.{name}"))
            {
                issues.Add(new($"{path}.{name}", "Required."));
            }

            return null;
        }

        if (Math.Abs(value.Value) > limit)
        {
            issues.Add(new($"{path}.{name}", $"{value} is outside the range -{limit} to {limit}."));
            return null;
        }

        return value;
    }
}
