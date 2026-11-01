using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// The identifier this package gives the platform for an input entry, so that a later run recognizes the observation it
/// created earlier (duplicate check, and completing a half-published entry). Derived from what identifies the entry
/// (species as in the input, the instant in UTC, the coordinates), so it stays the same across runs while the description
/// is free to change. It is a well-formed GUID (version and variant bits set), written in braces like the platform's own.
/// <para>
/// A user cannot tell it from the platform's own identifiers, and unlike a tag it cannot be edited away on a public page.
/// Different from the iNaturalist marker on purpose: the two platforms share no state.
/// </para>
/// </summary>
internal static class NaturguckerEntryId
{
    public static string For(ObservationCandidate candidate)
    {
        var key = string.Create(CultureInfo.InvariantCulture,
            $"naturgucker|{candidate.ScientificName}|{candidate.ObservedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffffffZ}|{candidate.Latitude:R}|{candidate.Longitude:R}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{{{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}}}";
    }

    /// <summary>True when the identifier <paramref name="reported"/> by the platform is <paramref name="id"/>, whatever the case and braces.</summary>
    public static bool Matches(string? reported, string id) =>
        reported is not null && string.Equals(Strip(reported), Strip(id), StringComparison.OrdinalIgnoreCase);

    private static string Strip(string value) => value.Trim().Trim('{', '}');
}
