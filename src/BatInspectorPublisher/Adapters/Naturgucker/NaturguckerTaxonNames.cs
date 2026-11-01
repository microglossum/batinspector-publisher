using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// The platform-specific taxonomy lags behind iNaturalist and the current research, which the input file follows. Where a name
/// of the input file differs from the platform-specific name for the same taxon, the adapter translates it, from a small fixed
/// table. This is deliberate and the only way a name is changed: a name that is not in the table is looked up as it is
/// and never replaced by a similar one.
/// <para>
/// Every pair was seen on the platform's own pages (see <c>docs/bat-taxonomy-europe.md</c>) and is to be confirmed by a
/// live test. When the platform changes its taxonomy, or the input file changes a name, the table is maintained by hand.
/// </para>
/// </summary>
internal static class NaturguckerTaxonNames
{
    private static readonly Dictionary<string, string> PlatformSpecificNames = new(StringComparer.Ordinal)
    {
        // The platform files both serotines under Eptesicus; current research and iNaturalist use Cnephaeus.
        ["Cnephaeus"] = "Eptesicus",
        ["Cnephaeus nilssonii"] = "Eptesicus nilssonii",
        ["Cnephaeus serotinus"] = "Eptesicus serotinus",

        // The platform has two entries for the lesser mouse-eared bat; the one in use is "Myotis blythii" (with the common
        // name and sightings since 2011), the one named oxygnathus has none. An older name of the input file goes to the used one.
        ["Myotis oxygnathus"] = "Myotis blythii",
    };

    /// <summary>
    /// The platform-specific taxon name for <paramref name="scientificName"/>: normalized to binomial capitalization and, if the
    /// table has a pair for it, translated. A name without a pair is returned normalized and otherwise unchanged.
    /// </summary>
    public static string ToPlatformSpecificTaxonName(string scientificName)
    {
        var name = ScientificName.Normalize(scientificName);
        return PlatformSpecificNames.TryGetValue(name, out var specificName) ? specificName : name;
    }
}
