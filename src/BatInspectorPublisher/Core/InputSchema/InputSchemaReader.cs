using System.Globalization;
using System.Text.Json;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Validation;

namespace BatInspectorPublisher.Core.InputSchema;

/// <summary>
/// Reads the BatInspector export file (<c>{ "SchemaVersion": 1, "DocumentFiles": [ ... ] }</c>).
/// Strict about required fields and the version, tolerant of unknown properties (additive changes
/// stay compatible within one schema version). Does not touch the file system for evidence files;
/// the referenced spectrogram/audio is read and checked once per candidate at publish time.
/// <para>
/// Validation is per entry. A file that is not well-formed, or has a missing or unsupported
/// <c>SchemaVersion</c> or no <c>DocumentFiles</c> array, is rejected as a whole (an exception).
/// An invalid entry is left out of <see cref="InputDocument.Candidates"/> and reported in
/// <see cref="InputDocument.Rejected"/>; the valid entries are still returned.
/// </para>
/// </summary>
public static class InputSchemaReader
{
    /// <summary>Highest schema version this reader understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Date format used by BatInspector, German local time without zone.</summary>
    internal const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    /// <summary>Reads and validates a file.</summary>
    /// <exception cref="InputSchemaException">The file is not well-formed JSON, or its structure or version is invalid.</exception>
    /// <exception cref="TimeZoneNotFoundException">The host has no Europe/Berlin time zone data.</exception>
    public static InputDocument ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads and validates a stream (UTF-8 JSON).</summary>
    /// <exception cref="InputSchemaException">The content is not well-formed JSON, or its structure or version is invalid.</exception>
    /// <exception cref="TimeZoneNotFoundException">The host has no Europe/Berlin time zone data.</exception>
    public static InputDocument Read(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses and validates a JSON string.</summary>
    /// <exception cref="InputSchemaException">The content is not well-formed JSON, or its structure or version is invalid.</exception>
    /// <exception cref="TimeZoneNotFoundException">The host has no Europe/Berlin time zone data.</exception>
    public static InputDocument Parse(string json) => Parse(json, TimeProvider.System);

    internal static InputDocument Parse(string json, TimeProvider time)
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

            var rejected = new List<RejectedEntry>();
            var warnings = new List<EntryWarning>();
            var candidates = ReadCandidates(root, issues, rejected, warnings, new ZoneResolver(), time.GetUtcNow());
            if (issues.Count > 0)
            {
                throw new InputSchemaException(issues);
            }

            return new InputDocument(version.Value, candidates) { Rejected = rejected, Warnings = warnings };
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

    /// <summary>Reads all entries. Structural problems go to <paramref name="issues"/>, per-entry problems to <paramref name="rejected"/>.</summary>
    private static List<ObservationCandidate> ReadCandidates(
        JsonElement root, List<ValidationIssue> issues, List<RejectedEntry> rejected, List<EntryWarning> warnings, ZoneResolver zones, DateTimeOffset now)
    {
        var candidates = new List<ObservationCandidate>();
        if (!root.TryGetProperty("DocumentFiles", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("DocumentFiles", "Required array."));
            return candidates;
        }

        var duplicates = new DuplicateEntryFinder();
        var index = -1;
        foreach (var entry in files.EnumerateArray())
        {
            index++;
            var path = $"DocumentFiles[{index}]";
            var entryIssues = new List<ValidationIssue>();
            if (entry.ValueKind != JsonValueKind.Object)
            {
                rejected.Add(new(index, [new(path, "Must be an object.")]));
                continue;
            }

            var species = RequiredString(entry, "SpeciesLatin", path, entryIssues);
            var entryWarnings = new List<ValidationIssue>();
            var zone = OptionalZone(entry, zones, path, entryIssues);
            var observedAt = RequiredDate(entry, "Date", zone?.Zone, path, entryIssues, entryWarnings);
            var lat = RequiredNumber(entry, "Latitude", path, entryIssues);
            var lon = RequiredNumber(entry, "Longitude", path, entryIssues);
            var png = RequiredString(entry, "PathToPng", path, entryIssues);
            var wav = RequiredString(entry, "PathToWav", path, entryIssues);
            var temperature = OptionalNumber(entry, "Temperature", path, entryIssues);
            var humidity = OptionalNumber(entry, "Humidity", path, entryIssues);
            var local = OptionalString(entry, "SpeciesLocal", path, entryIssues);
            var comment = OptionalString(entry, "Comment", path, entryIssues);

            // Reading is done; whether the values are acceptable is the validator's call.
            foreach (var issue in EntryValidator.ValidateValues(new EntryValues(observedAt, lat, lon, png, wav), now))
            {
                entryIssues.Add(issue with { Path = $"{path}.{InputFieldName(issue.Path)}" });
            }

            if (entryIssues.Count > 0)
            {
                rejected.Add(new(index, entryIssues));
                continue;
            }

            // Measurements only enrich the description: an implausible one is dropped with a warning, not a rejection.
            var plausibility = new List<ValidationIssue>();
            temperature = EntryValidator.PlausibleTemperature(temperature, plausibility);
            humidity = EntryValidator.PlausibleHumidity(humidity, plausibility);
            var name = ScientificName.Normalize(species!);
            plausibility.AddRange(EntryValidator.ValidatePlausibility(name, observedAt));
            entryWarnings.AddRange(plausibility.Select(w => w with { Path = $"{path}.{InputFieldName(w.Path)}" }));

            var candidate = new ObservationCandidate
            {
                EntryIndex = index,
                ScientificName = name,
                LocalName = local,
                ObservedAt = observedAt!.Value,
                TimeZoneId = zone!.Value.Id,
                Latitude = lat!.Value,
                Longitude = lon!.Value,
                TemperatureCelsius = temperature,
                HumidityPercent = humidity,
                Comment = comment,
                SpectrogramPath = png!,
                AudioPath = wav!,
            };

            if (duplicates.Check(index, path, candidate) is { } duplicate)
            {
                entryWarnings.Add(duplicate);
            }

            warnings.AddRange(entryWarnings.Select(w => new EntryWarning(index, w)));
            candidates.Add(candidate);
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

    private static DateTimeOffset? RequiredDate(
        JsonElement obj, string name, TimeZoneInfo? zone, string path, List<ValidationIssue> issues, List<ValidationIssue> warnings)
    {
        var text = RequiredString(obj, name, path, issues);
        if (text is null || zone is null)
        {
            return null;
        }

        if (!DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            issues.Add(new($"{path}.{name}", $"'{text}' does not match the format {DateFormat} (e.g. 13.06.2026 04:26:34)."));
            return null;
        }

        if (BerlinTime.TryConvert(zone, date, out var observedAt) is { } problem)
        {
            issues.Add(new($"{path}.{name}", $"'{text}' {problem}"));
            return null;
        }

        if (zone.IsAmbiguousTime(date))
        {
            warnings.Add(new($"{path}.{name}",
                $"'{text}' happens twice in {zone.Id} (clocks go back); read as standard time. Check the time."));
        }

        return observedAt;
    }

    /// <summary>Reads the optional <c>TimeZone</c> (IANA id); without it the entry is German local time.</summary>
    private static (TimeZoneInfo Zone, string Id)? OptionalZone(JsonElement entry, ZoneResolver zones, string path, List<ValidationIssue> issues)
    {
        var id = OptionalString(entry, "TimeZone", path, issues);
        if (id is null)
        {
            return issues.Any(i => i.Path == $"{path}.TimeZone") ? null : (zones.Default, BerlinTime.DefaultZoneId);
        }

        var zone = zones.Find(id);
        if (zone is null)
        {
            issues.Add(new($"{path}.TimeZone", $"'{id}' is not a known time zone. Use an IANA id such as Europe/Berlin."));
        }

        return zone is null ? null : (zone, id);
    }

    /// <summary>Looks zones up once per file; the default zone is only required when an entry needs it.</summary>
    private sealed class ZoneResolver
    {
        private readonly Dictionary<string, TimeZoneInfo?> _byId = new(StringComparer.Ordinal);
        private TimeZoneInfo? _default;

        public TimeZoneInfo Default => _default ??= BerlinTime.FindZone();

        public TimeZoneInfo? Find(string id)
        {
            if (!_byId.TryGetValue(id, out var zone))
            {
                _byId[id] = zone = BerlinTime.TryFindZone(id);
            }

            return zone;
        }
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

    private static double? RequiredNumber(JsonElement obj, string name, string path, List<ValidationIssue> issues)
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

        return value;
    }

    /// <summary>The validator names values after <see cref="ObservationCandidate"/> properties; the file calls some of them differently.</summary>
    private static string InputFieldName(string candidateProperty) => candidateProperty switch
    {
        nameof(ObservationCandidate.ObservedAt) => "Date",
        nameof(ObservationCandidate.ScientificName) => "SpeciesLatin",
        nameof(ObservationCandidate.TemperatureCelsius) => "Temperature",
        nameof(ObservationCandidate.HumidityPercent) => "Humidity",
        nameof(ObservationCandidate.SpectrogramPath) => "PathToPng",
        nameof(ObservationCandidate.AudioPath) => "PathToWav",
        _ => candidateProperty,
    };
}
