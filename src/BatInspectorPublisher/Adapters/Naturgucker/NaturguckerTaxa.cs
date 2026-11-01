using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>The platform's taxon entry an observation is filed under, as the lookup asks for it: genus and epithet.</summary>
/// <param name="Genus">Genus, or a combined genus entry of the platform, or the order.</param>
/// <param name="Epithet">Species epithet, or <see cref="NaturguckerTaxa.Undetermined"/> for the entry "of the genus, species not determined".</param>
/// <param name="Uncertain">True when the identification is only as precise as a genus or a group.</param>
internal readonly record struct NaturguckerTaxon(string Genus, string Epithet, bool Uncertain)
{
    /// <summary>The entry as the platform shows it, for messages: "Pipistrellus nathusii" or "Pipistrellus indet.".</summary>
    public override string ToString() => $"{Genus} {Epithet}";
}

/// <summary>
/// Decides where an input name goes on the platform. The identification BatInspector delivers is trusted (its users are
/// experts): a species name is filed as a certain identification, and where they are unsure they write a genus or a group
/// value instead, which is filed under the platform's entry for the genus or the group, flagged uncertain. A subspecies
/// is not published. Nothing else is filed under a guess: a name that is not a bat is skipped, never filed under the order.
/// </summary>
internal static class NaturguckerTaxa
{
    /// <summary>What the platform calls the entry "species of this genus not determined".</summary>
    public const string Undetermined = "indet.";

    private const string Bats = "Chiroptera";

    /// <summary>
    /// BatInspector group and uncertain-call values that are not taxon names, and the platform's entry each is filed under:
    /// its combined genus entry for the nyctaloid bats, the genus entry for Myotis, the order for the rest. "todo" (not yet
    /// reviewed in BatInspector) is deliberately not listed.
    /// </summary>
    private static readonly Dictionary<string, string> GroupValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nyctaloid"] = "Eptesicus/Nyctalus/Vespertilio",
        ["Social"] = Bats,
        ["?"] = Bats,
        ["Mbart"] = "Myotis",
    };

    /// <summary>
    /// True when <paramref name="scientificName"/> can be published: <paramref name="taxon"/> is then the platform's entry.
    /// Otherwise <paramref name="rejection"/> says why not, as the status and the English message the result carries.
    /// </summary>
    public static bool TryResolve(string scientificName, out NaturguckerTaxon taxon, out NaturguckerValidator.Rejection rejection)
    {
        taxon = default;
        rejection = default;

        if (GroupValues.TryGetValue(scientificName.Trim(), out var group))
        {
            taxon = new(group, Undetermined, Uncertain: true);
            return true;
        }

        var name = NaturguckerTaxonNames.ToPlatformSpecificTaxonName(scientificName);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 2)
        {
            rejection = new(PublishStatus.SkippedInvalidEntry,
                $"'{scientificName}' is not a species or genus name: the platform takes species, genera and groups, not subspecies.");
            return false;
        }

        if (words.Length == 0 || !IsBatName(words[0]))
        {
            // Fail closed: only a known bat genus is published, whatever the lookup would answer.
            rejection = new(PublishStatus.SkippedUnresolvedTaxon,
                $"'{scientificName}' is not a genus this adapter knows as a bat; nothing is published. " +
                "If it is a bat, the genus list in the package needs an update.");
            return false;
        }

        taxon = words.Length == 2 ? new(words[0], words[1], Uncertain: false) : new(words[0], Undetermined, Uncertain: true);
        return true;
    }

    private static bool IsBatName(string genus) =>
        string.Equals(genus, Bats, StringComparison.Ordinal) || NaturguckerBatGenera.IsBat(genus);
}
