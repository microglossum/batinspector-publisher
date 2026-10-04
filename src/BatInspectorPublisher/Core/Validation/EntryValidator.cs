using System.Globalization;
using BatInspectorPublisher.Core.InputSchema;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Core.Validation;

/// <summary>The values of one entry that <see cref="EntryValidator"/> judges. Each is null when it could not be read.</summary>
internal readonly record struct EntryValues(
    DateTimeOffset? ObservedAt, double? Latitude, double? Longitude, string? SpectrogramPath, string? AudioPath)
{
    /// <summary>The values of a candidate that was built in code instead of read from a file.</summary>
    public static EntryValues From(ObservationCandidate candidate) =>
        new(candidate.ObservedAt, candidate.Latitude, candidate.Longitude, candidate.SpectrogramPath, candidate.AudioPath);
}

/// <summary>
/// All platform-neutral validation in one place: what makes an entry wrong or unusable whatever platform it goes to.
/// The reader only reads (types, formats, zones) and the evidence loader only does file I/O; both ask this class
/// whether what they got is acceptable. What only one platform rejects (its limits, its accepted ranges) is
/// validated by that platform's adapter (see <c>INaturalistValidator</c>), never here.
/// <para>
/// Issue paths are the <see cref="ObservationCandidate"/> property names (<c>Latitude</c>, <c>SpectrogramPath</c>);
/// a caller that reads another format maps them to its own field names.
/// </para>
/// </summary>
internal static class EntryValidator
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Judges the values that could be read; a value that is null is the reader's problem and is skipped.
    /// Reports every problem found, not only the first.
    /// </summary>
    public static List<ValidationIssue> ValidateValues(EntryValues values, DateTimeOffset now)
    {
        var issues = new List<ValidationIssue>();
        CheckCoordinate(values.Latitude, nameof(EntryValues.Latitude), 90, issues);
        CheckCoordinate(values.Longitude, nameof(EntryValues.Longitude), 180, issues);
        CheckEvidencePath(values.SpectrogramPath, nameof(EntryValues.SpectrogramPath), ".png", issues);
        CheckEvidencePath(values.AudioPath, nameof(EntryValues.AudioPath), ".wav", issues);

        // A position of exactly 0, 0 is a missing GPS fix, and a recording cannot come from the future.
        if (values.Latitude == 0 && values.Longitude == 0)
        {
            issues.Add(new(nameof(EntryValues.Latitude), "Latitude and Longitude are both 0: the GPS position is missing."));
        }

        if (values.ObservedAt > now)
        {
            issues.Add(new(nameof(EntryValues.ObservedAt), $"{values.ObservedAt.Value:dd.MM.yyyy HH:mm:ss} is in the future."));
        }

        return issues;
    }

    /// <summary>
    /// Air temperatures (°C) a recorder can plausibly measure. Sensors report error codes such as -127 or 85 that fall
    /// outside this range.
    /// </summary>
    private const double MinTemperature = -40, MaxTemperature = 60;

    /// <summary>First and last local hour (inclusive) at which a recording counts as made in daylight.</summary>
    private const int FirstDaylightHour = 9, LastDaylightHour = 15;

    /// <summary>
    /// Returns the temperature, or null with a warning when it is implausible. Measurements only enrich the description,
    /// so a doubtful one is left out and reported instead of rejecting the whole entry.
    /// </summary>
    public static double? PlausibleTemperature(double? value, List<ValidationIssue> warnings)
    {
        if (value is { } v && v is < MinTemperature or > MaxTemperature)
        {
            warnings.Add(new(nameof(ObservationCandidate.TemperatureCelsius),
                $"{v.ToString(CultureInfo.InvariantCulture)} °C is outside {MinTemperature} to {MaxTemperature} °C; the value is left out."));
            return null;
        }

        return value;
    }

    /// <summary>Returns the relative humidity, or null with a warning when it is not a percentage (0 to 100).</summary>
    public static double? PlausibleHumidity(double? value, List<ValidationIssue> warnings)
    {
        if (value is { } v && v is < 0 or > 100)
        {
            warnings.Add(new(nameof(ObservationCandidate.HumidityPercent),
                $"{v.ToString(CultureInfo.InvariantCulture)} % is outside 0 to 100 %; the value is left out."));
            return null;
        }

        return value;
    }

    /// <summary>
    /// Doubtful but publishable: a recording time in daylight (bats fly at night, so the device clock or the zone may be
    /// wrong) and a name that is neither a binomial nor a single genus word (it will not resolve to a taxon).
    /// </summary>
    public static List<ValidationIssue> ValidatePlausibility(string? scientificName, DateTimeOffset? observedAt)
    {
        var warnings = new List<ValidationIssue>();
        if (observedAt is { Hour: >= FirstDaylightHour and <= LastDaylightHour } local)
        {
            warnings.Add(new(nameof(ObservationCandidate.ObservedAt),
                $"{local:HH:mm} local time is in daylight; bats fly at night. Check the device clock and the time zone."));
        }

        if (scientificName is not null)
        {
            var words = scientificName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words > 2)
            {
                warnings.Add(new(nameof(ObservationCandidate.ScientificName),
                    $"'{scientificName}' is neither a genus nor a binomial species name; it will not resolve to a taxon."));
            }
        }

        return warnings;
    }

    /// <summary>
    /// Judges the content of an evidence file that was read. Returns null when it is fine, otherwise what is wrong
    /// ("empty", "not a PNG image") so that the caller can say "The spectrogram file is {problem}: {path}".
    /// A check of the first bytes, not a format validation: a file can start like a PNG and still be something else.
    /// </summary>
    public static string? ValidateEvidenceContent(EvidenceKind kind, ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            return "empty";
        }

        return kind switch
        {
            EvidenceKind.Spectrogram when !content.StartsWith(PngSignature) => "not a PNG image",
            // RIFF container with a WAVE form type: "RIFF" <size> "WAVE".
            EvidenceKind.Audio when !IsWav(content) => "not a WAV recording",
            _ => null,
        };
    }

    private static void CheckCoordinate(double? value, string name, double limit, List<ValidationIssue> issues)
    {
        if (value is { } v && Math.Abs(v) > limit)
        {
            issues.Add(new(name, $"{v.ToString(CultureInfo.InvariantCulture)} is outside the range -{limit} to {limit}."));
        }
    }

    /// <summary>
    /// Evidence paths are uploaded to a public service, so they must be explicit: absolute (never resolved against the
    /// process working directory) and of the expected type. The syntax check accepts Windows and Unix absolute paths on
    /// every OS (the file may be written on another OS).
    /// </summary>
    private static void CheckEvidencePath(string? value, string name, string extension, List<ValidationIssue> issues)
    {
        if (value is null)
        {
            return;
        }

        if (!IsAbsolutePath(value))
        {
            issues.Add(new(name, $"'{value}' is not an absolute path."));
        }

        if (!HasExtension(value, extension))
        {
            issues.Add(new(name, $"'{value}' must be a {extension} file."));
        }
    }

    private static bool IsAbsolutePath(string value) =>
        value.StartsWith('/')
        || value.StartsWith(@"\\", StringComparison.Ordinal)
        || (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/');

    private static bool HasExtension(string value, string extension)
    {
        var name = value[(value.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && name.Length > extension.Length;
    }

    private static bool IsWav(ReadOnlySpan<byte> content) =>
        content.Length >= 12
        && content[..4].SequenceEqual("RIFF"u8)
        && content.Slice(8, 4).SequenceEqual("WAVE"u8);
}

/// <summary>Which evidence file is being judged.</summary>
internal enum EvidenceKind
{
    /// <summary>The spectrogram image (PNG).</summary>
    Spectrogram,

    /// <summary>The audio recording (WAV).</summary>
    Audio,
}
