using BatInspectorPublisher.Adapters.Naturgucker;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerBatGeneraTests
{
    [Theory]
    [InlineData("Pipistrellus nathusii")]
    [InlineData("pipistrellus NATHUSII")]
    [InlineData("  Myotis   myotis ")]
    [InlineData("Plecotus")]
    [InlineData("Eptesicus nilssonii")]
    [InlineData("Hypsugo savii")]
    public void IsBat_ListedGenus_IsTrue(string name)
    {
        Assert.True(NaturguckerBatGenera.IsBat(name));
    }

    [Theory]
    [InlineData("Parus major")]
    [InlineData("Chiroptera")]
    [InlineData("Cnephaeus serotinus")]
    [InlineData("Myotisx daubentonii")]
    [InlineData("Eptesicus-serotinus")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBat_UnlistedGenusOrNoName_IsFalse(string name)
    {
        Assert.False(NaturguckerBatGenera.IsBat(name));
    }
}
