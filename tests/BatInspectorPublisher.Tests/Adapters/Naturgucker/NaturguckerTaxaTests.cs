using BatInspectorPublisher.Adapters.Naturgucker;
using BatInspectorPublisher.Core.Results;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerTaxaTests
{
    private static NaturguckerTaxon Resolve(string name)
    {
        Assert.True(NaturguckerTaxa.TryResolve(name, out var taxon, out var rejection), rejection.Message);
        return taxon;
    }

    private static NaturguckerValidator.Rejection Reject(string name)
    {
        Assert.False(NaturguckerTaxa.TryResolve(name, out _, out var rejection));
        return rejection;
    }

    [Theory]
    [InlineData("Pipistrellus nathusii", "Pipistrellus", "nathusii")]
    [InlineData("pipistrellus NATHUSII", "Pipistrellus", "nathusii")]
    [InlineData("Plecotus auritus", "Plecotus", "auritus")]
    public void TryResolve_Species_IsACertainIdentification(string name, string genus, string epithet)
    {
        Assert.Equal(new NaturguckerTaxon(genus, epithet, Uncertain: false), Resolve(name));
    }

    [Theory]
    [InlineData("Myotis", "Myotis")]
    [InlineData("pipistrellus", "Pipistrellus")]
    [InlineData("Chiroptera", "Chiroptera")]
    public void TryResolve_Genus_IsTheGenusEntryAndUncertain(string name, string genus)
    {
        Assert.Equal(new NaturguckerTaxon(genus, NaturguckerTaxa.Undetermined, Uncertain: true), Resolve(name));
    }

    [Theory]
    [InlineData("Nyctaloid", "Eptesicus/Nyctalus/Vespertilio")]
    [InlineData("nyctaloid", "Eptesicus/Nyctalus/Vespertilio")]
    [InlineData("Social", "Chiroptera")]
    [InlineData("?", "Chiroptera")]
    [InlineData("Mbart", "Myotis")]
    public void TryResolve_GroupValue_IsTheGroupEntryAndUncertain(string name, string genus)
    {
        Assert.Equal(new NaturguckerTaxon(genus, NaturguckerTaxa.Undetermined, Uncertain: true), Resolve(name));
    }

    [Fact]
    public void TryResolve_NameOfTheInputFileThePlatformSpellsDifferently_IsTranslated()
    {
        Assert.Equal(new NaturguckerTaxon("Eptesicus", "serotinus", Uncertain: false), Resolve("Cnephaeus serotinus"));
        Assert.Equal(new NaturguckerTaxon("Eptesicus", NaturguckerTaxa.Undetermined, Uncertain: true), Resolve("Cnephaeus"));
    }

    [Fact]
    public void TryResolve_OlderNameOfTheLesserMouseEaredBat_GoesToTheEntryInUse()
    {
        Assert.Equal(new NaturguckerTaxon("Myotis", "blythii", Uncertain: false), Resolve("Myotis oxygnathus"));
    }

    [Fact]
    public void TryResolve_NameWithoutAPair_IsLookedUpAsItIs()
    {
        Assert.Equal(new NaturguckerTaxon("Myotis", "blythii", Uncertain: false), Resolve("Myotis blythii"));
    }

    [Theory]
    [InlineData("Parus major")]
    [InlineData("Parus")]
    [InlineData("Cnephaeus pachyomus")]
    [InlineData("todo")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_NotABat_IsSkippedAsUnresolvedTaxonNeverFiledUnderTheOrder(string name)
    {
        var rejection = Reject(name);

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, rejection.Status);
    }

    [Theory]
    [InlineData("Vespertilio murinus murinus")]
    [InlineData("Myotis myotis oxygnathus")]
    [InlineData("Vespertilio murinus subsp. murinus")]
    public void TryResolve_Subspecies_IsAnInvalidEntryForThisPlatform(string name)
    {
        var rejection = Reject(name);

        Assert.Equal(PublishStatus.SkippedInvalidEntry, rejection.Status);
        Assert.Contains(name, rejection.Message);
    }

    [Fact]
    public void ToString_ShowsTheEntryLikeThePlatformDoes()
    {
        Assert.Equal("Pipistrellus indet.", Resolve("Pipistrellus").ToString());
        Assert.Equal("Pipistrellus nathusii", Resolve("Pipistrellus nathusii").ToString());
    }
}
