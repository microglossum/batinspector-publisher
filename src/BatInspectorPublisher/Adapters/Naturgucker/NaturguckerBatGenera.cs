using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// The platform cannot tell this package whether a species is a bat, so the adapter checks the genus against a small
/// fixed list before any request. Fail closed: a genus that is not on the list is never published; the entry is
/// reported and the list is extended in a package release after a check by hand.
/// <para>
/// The list covers the genera of the bats found in Central Europe, as the platform spells them (seen on its pages
/// 2026-11-01). It holds genera only, never species, so a taxonomic split or rename inside a genus does not make it stale.
/// It is checked against the platform-specific taxon name (<see cref="NaturguckerTaxonNames.ToPlatformSpecificTaxonName"/>): <c>Cnephaeus</c> of the
/// input file is <c>Eptesicus</c> here, and <c>Cnephaeus</c> itself is not on the list because the platform has no such genus.
/// </para>
/// <para>
/// Not verified against the platform's own species list: the list is general taxonomic knowledge. Whether a name exists
/// on the platform is decided live when the species is resolved; this list only answers "is it a bat". It must be
/// maintained by hand whenever the global taxonomy or the platform's changes a genus (a split, a merge, a rename).
/// </para>
/// </summary>
internal static class NaturguckerBatGenera
{
    private static readonly HashSet<string> Genera = new(StringComparer.Ordinal)
    {
        "Barbastella",
        "Eptesicus",
        "Hypsugo",
        "Miniopterus",
        "Myotis",
        "Nyctalus",
        "Pipistrellus",
        "Plecotus",
        "Rhinolophus",
        "Tadarida",
        "Vespertilio",
    };

    /// <summary>True when the first word of <paramref name="scientificName"/> (case-insensitive) is a listed bat genus.</summary>
    public static bool IsBat(string scientificName) =>
        Genus(scientificName) is { } genus && Genera.Contains(genus);

    private static string? Genus(string scientificName) =>
        ScientificName.WordCount(scientificName) == 0 ? null : ScientificName.Normalize(scientificName).Split(' ')[0];
}
