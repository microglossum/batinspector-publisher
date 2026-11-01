using BatInspectorPublisher.Adapters.Naturgucker;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerTaxonNamesTests
{
    [Theory]
    [InlineData("Cnephaeus serotinus", "Eptesicus serotinus")]
    [InlineData("Cnephaeus nilssonii", "Eptesicus nilssonii")]
    [InlineData("Cnephaeus", "Eptesicus")]
    [InlineData("Myotis oxygnathus", "Myotis blythii")]
    public void ToPlatformSpecificTaxonName_NameWithAPair_IsTranslated(string name, string expected)
    {
        Assert.Equal(expected, NaturguckerTaxonNames.ToPlatformSpecificTaxonName(name));
    }

    [Theory]
    [InlineData("cnephaeus SEROTINUS", "Eptesicus serotinus")]
    [InlineData("  cnephaeus   NILSSONII ", "Eptesicus nilssonii")]
    public void ToPlatformSpecificTaxonName_FindsThePairWhateverTheCapitalizationAndSpacing(string name, string expected)
    {
        Assert.Equal(expected, NaturguckerTaxonNames.ToPlatformSpecificTaxonName(name));
    }

    [Theory]
    [InlineData("Eptesicus serotinus")]
    [InlineData("Myotis blythii")]
    [InlineData("Pipistrellus pipistrellus")]
    public void ToPlatformSpecificTaxonName_NameThePlatformKnows_IsUnchanged(string name)
    {
        Assert.Equal(name, NaturguckerTaxonNames.ToPlatformSpecificTaxonName(name));
    }

    [Theory]
    [InlineData("Myotis oxygnatus")]
    [InlineData("Cnephaeus pachyomus")]
    [InlineData("Cnephaeus fuscus")]
    public void ToPlatformSpecificTaxonName_NameWithoutAPair_IsNeverReplacedBySimilarOne(string name)
    {
        Assert.Equal(name, NaturguckerTaxonNames.ToPlatformSpecificTaxonName(name));
    }
}
