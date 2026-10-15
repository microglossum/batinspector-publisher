using BatInspectorPublisher.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class INaturalistTokenTests
{
    [Fact]
    public void ToString_ShowsUserAndExpiryButNoToken()
    {
        var token = new INaturalistToken
        {
            AccessToken = "secret-jwt",
            OAuthAccessToken = "secret-oauth",
            RefreshToken = "secret-refresh",
            Username = "bat-fan",
            ObtainedAtUtc = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero),
            ExpiresInSeconds = 24 * 3600,
        };

        var text = token.ToString();

        Assert.Contains("bat-fan", text);
        Assert.Contains("2026", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("AccessToken", text);
    }
}
