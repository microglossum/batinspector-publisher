using System.Runtime.Versioning;
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

    [Fact]
    public void CanSave_ReflectsPlatformAndOptIn()
    {
        Assert.True(new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true).CanSave);
        Assert.Equal(OperatingSystem.IsWindows(), new ProtectedFileTokenStore(_dir).CanSave);
    }

    [Fact]
    public void Load_CorruptFile_IsNoToken_AndNextSaveRepairsIt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "oauthtoken.dat"), "{ this is not a token");
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);

        Assert.Null(store.Load());

        store.Save(Token());
        Assert.Equal(Token(), store.Load());
    }

    [Fact]
    public void Load_EmptyFile_IsNoToken()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "oauthtoken.dat"), []);

        Assert.Null(new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true).Load());
    }

    [Fact]
    public void Load_StaleTempFileFromInterruptedWrite_IsIgnored()
    {
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);
        store.Save(Token());
        File.WriteAllText(Path.Combine(_dir, "oauthtoken.dat.0123.tmp"), "half a tok");

        Assert.Equal(Token(), store.Load());
    }

    [Fact]
    public void Save_WhenTheFinalMoveFails_LeavesNoTempFileBehind()
    {
        // A directory in place of the token file makes the move fail after the temp file was written.
        Directory.CreateDirectory(Path.Combine(_dir, "oauthtoken.dat"));
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);

        Assert.ThrowsAny<Exception>(() => store.Save(Token()));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_Twice_ReplacesTheTokenAndLeavesOneFile()
    {
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);
        store.Save(Token());
        store.Save(Token() with { Username = "other" });

        Assert.Equal("other", store.Load()!.Username);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [UnixOnlyFact]
    [UnsupportedOSPlatform("windows")]
    public void Save_OnUnix_CreatesOwnerOnlyFile_AlsoWhenReplacing()
    {
        var store = new ProtectedFileTokenStore(_dir, allowPlaintextOnNonWindows: true);
        var file = Path.Combine(_dir, "oauthtoken.dat");

        store.Save(Token());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

        store.Save(Token());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
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
