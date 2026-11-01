using BatInspectorPublisher.Adapters.Naturgucker;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

public class NaturguckerOptionsTests
{
    private static NaturguckerOptions Valid() => new() { ServiceNumber = 4711, Email = "user@example.org", Password = "password" };

    [Fact]
    public void ValidateForPublishing_CompleteOptions_Passes()
    {
        Valid().ValidateForPublishing();
    }

    [Fact]
    public void ValidateForPublishing_NothingSet_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new NaturguckerOptions().ValidateForPublishing());
    }

    [Theory]
    [InlineData(0L, "user@example.org", "password", "ServiceNumber")]
    [InlineData(4711L, "", "password", "Email")]
    [InlineData(4711L, "   ", "password", "Email")]
    [InlineData(4711L, "not-an-address", "password", "Email")]
    [InlineData(4711L, "user@example.org", "", "Password")]
    public void ValidateForPublishing_MissingPart_NamesIt(long service, string email, string password, string parameter)
    {
        var options = new NaturguckerOptions { ServiceNumber = service, Email = email, Password = password };

        var ex = Assert.Throws<ArgumentException>(options.ValidateForPublishing);

        Assert.Equal(parameter, ex.ParamName);
    }

    [Fact]
    public void ValidateForPublishing_NegativeServiceNumber_IsAccepted()
    {
        new NaturguckerOptions { ServiceNumber = -4711, Email = "user@example.org", Password = "p" }.ValidateForPublishing();
    }

    [Theory]
    [InlineData("password", "5f4dcc3b5aa765d61d8327deb882cf99")]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    public void PasswordHash_IsLowerCaseHexOfTheMd5(string password, string expected)
    {
        Assert.Equal(expected, new NaturguckerOptions { Password = password }.PasswordHash());
    }

    [Fact]
    public void ToString_NeverContainsThePasswordOrTheAddress()
    {
        var text = Valid().ToString();

        Assert.DoesNotContain("password", text);
        Assert.DoesNotContain("user@example.org", text);
    }
}
