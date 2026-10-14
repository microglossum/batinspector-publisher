using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// The tag that marks an observation as the one this package created for an input entry, so that a later run recognizes
/// it and completes it when its evidence is missing. Derived from what identifies the entry (species as in the input,
/// the instant in UTC, the coordinates), so it stays the same across runs while the description is free to change.
/// <para>
/// It is visible on the public observation. A user who deletes it only turns resume off for that observation: it is then
/// treated like any other existing observation and left alone.
/// </para>
/// </summary>
internal static class ResumeMarker
{
    private const string Prefix = "batinspector:";

    /// <summary>Hex digits of the hash in the tag: 48 bits, ample within one user's observations of a taxon, day and place.</summary>
    private const int HexDigits = 12;

    /// <summary>Length of every marker, reserved in the tag list limits.</summary>
    public const int Length = 25;

    public static string For(ObservationCandidate candidate)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"{candidate.ScientificName}|{candidate.ObservedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}|{candidate.Latitude:R}|{candidate.Longitude:R}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Prefix + Convert.ToHexString(hash, 0, HexDigits / 2).ToLowerInvariant();
    }

    /// <summary>True when <paramref name="tags"/> (as the search returns them) hold <paramref name="marker"/>.</summary>
    public static bool IsIn(IEnumerable<string> tags, string marker) =>
        tags.Any(tag => string.Equals(tag.Trim(), marker, StringComparison.OrdinalIgnoreCase));
}
