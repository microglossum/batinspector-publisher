using BatInspectorPublisher.Adapters.INaturalist;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

public class ProtectedFileTokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "token-store-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static INaturalistToken Token() => new()
    {
        AccessToken = "jwt",
        RefreshToken = "refresh",
        Username = "bat-fan",
        ObtainedAtUtc = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        ExpiresInSeconds = 86400,
    };

    [Fact]
    public void Load_NothingStored_ReturnsNull()
    {
        Assert.Null(new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true).Load());
    }

    [Fact]
    public void SaveLoad_RoundTrips_AndCreatesDirectory()
    {
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);

        store.Save(Token());

        Assert.Equal(Token(), store.Load());
    }

    [Fact]
    public void Clear_RemovesToken_AndIsIdempotent()
    {
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);
        store.Save(Token());

        store.Clear();
        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_OnNonWindowsWithoutOptIn_RefusesToWritePlaintext()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() => new ProtectedFileTokenStore(_dir).Save(Token()));
        Assert.False(Directory.Exists(_dir));
    }

    [WindowsOnlyFact]
    public void Save_OnWindows_EncryptsTheFile()
    {
        new ProtectedFileTokenStore(_dir).Save(Token());

        var raw = File.ReadAllText(Path.Combine(_dir, "oauthtoken.dat"));
        Assert.DoesNotContain("refresh", raw);
        Assert.Equal(Token(), new ProtectedFileTokenStore(_dir).Load());
    }
}
