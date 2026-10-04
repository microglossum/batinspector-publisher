using System.Globalization;
using BatInspectorPublisher.Core.Models;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// iNaturalist's own rules for an observation and its evidence, checked before the login and before anything is
/// written. The platform-neutral rules live in the input reader; this is what only iNaturalist rejects. A request that
/// breaks one of these rules fails with an undocumented error text, and for the evidence it would fail only after the
/// observation was created, leaving a public observation without its photo or sound.
/// <para>
/// Sources (iNaturalist open source, read 2026-10-04, not verified live): the Rails <c>Observation</c> model validations
/// and column sizes (<c>species_guess</c> is <c>varchar(255)</c>; the model message says 256), the tag list rule, and the
/// forum for the 20 MB file limit. Rules that this package cannot break by construction are not checked:
/// <c>place_guess</c> and <c>observed_on_string</c> have fixed short formats, <c>positional_accuracy</c> is not sent,
/// the description has no known limit (a <c>text</c> column).
/// </para>
/// </summary>
internal static class INaturalistValidator
{
    private const int MaxTextLength = 255;
    private const int MaxTagListLength = 750;
    private const int MaxTagLength = 255;
    private const int MaxAgeYears = 130;

    /// <summary>Why a candidate must not be published, as the status and the English message the result carries.</summary>
    internal readonly record struct Rejection(PublishStatus Status, string Message);

    /// <summary>Returns null when the candidate and its evidence pass; otherwise every problem found, in one rejection.</summary>
    public static Rejection? Validate(ObservationCandidate candidate, EvidenceFiles evidence, INaturalistOptions options, DateTimeOffset now)
    {
        var entry = EntryProblems(candidate, options, now);
        if (entry.Count > 0)
        {
            return new(PublishStatus.SkippedInvalidEntry, "iNaturalist would reject this entry: " + string.Join(" ", entry));
        }

        var files = EvidenceProblems(evidence, options.MaxEvidenceBytes);
        return files.Count > 0
            ? new(PublishStatus.SkippedInvalidEvidence, "iNaturalist would reject the evidence: " + string.Join(" ", files))
            : null;
    }

    private static List<string> EntryProblems(ObservationCandidate candidate, INaturalistOptions options, DateTimeOffset now)
    {
        var problems = new List<string>();

        // Mirrors the server: an observation date after (UTC date of the request + 1 day) is refused. iNaturalist also refuses
        // a date after today in the account's own time zone, which is unknown here, so this only catches what no zone allows.
        var day = DateOnly.FromDateTime(candidate.ObservedAt.DateTime);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (day > today.AddDays(1))
        {
            problems.Add($"The date {Format(day)} is in the future.");
        }
        else if (day < today.AddYears(-MaxAgeYears))
        {
            problems.Add($"The date {Format(day)} is more than {MaxAgeYears} years ago.");
        }

        var (lat, lon) = (candidate.Latitude, candidate.Longitude);
        if (!double.IsFinite(lat) || lat <= -90 || lat >= 90)
        {
            problems.Add($"Latitude {Format(lat)} is not accepted; it must be greater than -90 and less than 90.");
        }

        if (!double.IsFinite(lon) || lon < -180 || lon > 180)
        {
            problems.Add($"Longitude {Format(lon)} is not accepted; it must be between -180 and 180.");
        }

        if (lat == 0 && lon == 0)
        {
            problems.Add("The position 0, 0 is not accepted (it means a missing GPS position).");
        }

        if (candidate.ScientificName.Length > MaxTextLength)
        {
            problems.Add($"The species name is longer than {MaxTextLength} characters.");
        }

        var tags = options.TagList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (string.Join(", ", tags).Length > MaxTagListLength)
        {
            problems.Add($"INaturalistOptions.TagList is longer than {MaxTagListLength} characters.");
        }

        if (tags.Any(tag => tag.Length > MaxTagLength))
        {
            problems.Add($"INaturalistOptions.TagList has a tag longer than {MaxTagLength} characters.");
        }

        return problems;
    }

    private static List<string> EvidenceProblems(EvidenceFiles evidence, long maxBytes)
    {
        var problems = new List<string>();
        CheckSize(evidence.Spectrogram, "spectrogram", "Use a smaller image; iNaturalist scales photos to 2048 pixels anyway.");
        CheckSize(evidence.Audio, "audio", "Shorten the recording or lower its sample rate.");
        return problems;

        void CheckSize(EvidenceFile file, string role, string advice)
        {
            if (file.Content.Length > maxBytes)
            {
                problems.Add($"The {role} file '{file.FileName}' is {Megabytes(file.Content.Length)} MB; iNaturalist accepts at most {Megabytes(maxBytes)} MB per file. {advice}");
            }
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}
