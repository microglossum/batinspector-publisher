using BatInspectorPublisher.Adapters.Naturgucker;
using BatInspectorPublisher.Core.Results;
using BatInspectorPublisher.Tests.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerValidatorTests
{
    [Fact]
    public void Validate_BatSpecies_Passes()
    {
        var candidate = TestData.Candidate() with { ScientificName = "Pipistrellus nathusii" };

        Assert.Null(NaturguckerValidator.Validate(candidate));
    }

    [Theory]
    [InlineData("Cnephaeus serotinus")]
    [InlineData("Cnephaeus nilssonii")]
    [InlineData("Cnephaeus")]
    [InlineData("Myotis oxygnathus")]
    public void Validate_NameOfTheInputFileThePlatformSpellsDifferently_Passes(string name)
    {
        var candidate = TestData.Candidate() with { ScientificName = name };

        Assert.Null(NaturguckerValidator.Validate(candidate));
    }

    [Theory]
    [InlineData("Cnephaeus pachyomus")]
    [InlineData("")]
    public void Validate_NameWithoutAKnownBatGenusAfterTranslation_IsSkipped(string name)
    {
        var candidate = TestData.Candidate() with { ScientificName = name };

        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, NaturguckerValidator.Validate(candidate)?.Status);
    }

    [Fact]
    public void Validate_NotABatGenus_IsSkippedAsUnresolvedTaxonAndNamesTheName()
    {
        var candidate = TestData.Candidate() with { ScientificName = "Parus major" };

        var rejection = NaturguckerValidator.Validate(candidate);

        Assert.NotNull(rejection);
        Assert.Equal(PublishStatus.SkippedUnresolvedTaxon, rejection.Value.Status);
        Assert.Contains("Parus major", rejection.Value.Message);
    }
}
