using System.Text.RegularExpressions;
using BatInspectorPublisher.Adapters.Naturgucker;
using BatInspectorPublisher.Tests.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerEntryIdTests
{
    [Fact]
    public void For_IsAWellFormedGuidInBraces()
    {
        var id = NaturguckerEntryId.For(TestData.Candidate());

        Assert.Matches(new Regex("^\\{[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\}$"), id);
        Assert.True(Guid.TryParse(id, out _));
    }

    [Fact]
    public void For_SameEntry_GivesTheSameIdEveryTime()
    {
        Assert.Equal(NaturguckerEntryId.For(TestData.Candidate()), NaturguckerEntryId.For(TestData.Candidate()));
    }

    [Fact]
    public void For_SameInstantInAnotherOffset_GivesTheSameId()
    {
        var candidate = TestData.Candidate();
        var shifted = candidate with { ObservedAt = candidate.ObservedAt.ToOffset(TimeSpan.Zero) };

        Assert.Equal(NaturguckerEntryId.For(candidate), NaturguckerEntryId.For(shifted));
    }

    [Fact]
    public void For_DescriptionAndRemark_DoNotChangeTheId()
    {
        var candidate = TestData.Candidate();

        Assert.Equal(NaturguckerEntryId.For(candidate), NaturguckerEntryId.For(candidate with { Comment = "another remark", LocalName = "another name" }));
    }

    [Fact]
    public void For_AnyIdentifyingFieldChanged_ChangesTheId()
    {
        var candidate = TestData.Candidate();
        var original = NaturguckerEntryId.For(candidate);

        Assert.NotEqual(original, NaturguckerEntryId.For(candidate with { ScientificName = candidate.ScientificName + " x" }));
        Assert.NotEqual(original, NaturguckerEntryId.For(candidate with { ObservedAt = candidate.ObservedAt.AddSeconds(1) }));
        Assert.NotEqual(original, NaturguckerEntryId.For(candidate with { Latitude = candidate.Latitude + 0.00001 }));
        Assert.NotEqual(original, NaturguckerEntryId.For(candidate with { Longitude = candidate.Longitude + 0.00001 }));
    }

    [Theory]
    [InlineData("{5B0E3A7C-92D4-4F18-A6C1-0D7E8F2B4A93}", "{5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93}")]
    [InlineData("5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93", "{5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93}")]
    [InlineData("  {5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93} ", "{5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93}")]
    public void Matches_IgnoresCaseBracesAndSpaces(string reported, string id)
    {
        Assert.True(NaturguckerEntryId.Matches(reported, id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    public void Matches_AnotherOrMissingId_IsFalse(string? reported)
    {
        Assert.False(NaturguckerEntryId.Matches(reported, "{5b0e3a7c-92d4-4f18-a6c1-0d7e8f2b4a93}"));
    }
}
