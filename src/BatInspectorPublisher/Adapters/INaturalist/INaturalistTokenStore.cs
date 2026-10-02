using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Persistence for the iNaturalist login. Implement it to keep tokens somewhere else (e.g. a host-managed vault).</summary>
public interface INaturalistTokenStore
{
    /// <summary>Returns the stored token or null if there is none.</summary>
    INaturalistToken? Load();

    /// <summary>Stores the token, replacing any previous one.</summary>
    void Save(INaturalistToken token);

    /// <summary>Removes the stored token.</summary>
    void Clear();
}

/// <summary>
/// Stores the token in a file. On Windows it is DPAPI-encrypted for the current user. DPAPI does
/// not exist elsewhere, and silently writing a refresh token as plain text is not acceptable for a
/// public package, so on other platforms saving throws <see cref="PlatformNotSupportedException"/>
/// unless <c>allowPlaintextOnNonWindows</c> is set explicitly.
/// </summary>
public sealed class ProtectedFileTokenStore : INaturalistTokenStore
{
    private readonly string _filePath;
    private readonly bool _allowPlaintext;

    /// <summary>Uses <c>%LOCALAPPDATA%/BatInspectorPublisher/inaturalist/oauthtoken.dat</c> (or the platform equivalent).</summary>
    public ProtectedFileTokenStore(bool allowPlaintextOnNonWindows = false)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatInspectorPublisher", "inaturalist"), allowPlaintextOnNonWindows)
    {
    }

    /// <summary>Uses <paramref name="directory"/> for the token file.</summary>
    public ProtectedFileTokenStore(string directory, bool allowPlaintextOnNonWindows = false)
    {
        _filePath = Path.Combine(directory, "oauthtoken.dat");
        _allowPlaintext = allowPlaintextOnNonWindows;
    }

    /// <inheritdoc />
    public INaturalistToken? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var stored = File.ReadAllBytes(_filePath);
        var plain = OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(stored, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : stored;
        return JsonSerializer.Deserialize<INaturalistToken>(Encoding.UTF8.GetString(plain));
    }

    /// <inheritdoc />
    public void Save(INaturalistToken token)
    {
        if (!OperatingSystem.IsWindows() && !_allowPlaintext)
        {
            throw new PlatformNotSupportedException(
                "Encrypted token storage is only available on Windows. Pass allowPlaintextOnNonWindows: true to store the token unencrypted, or supply your own INaturalistTokenStore.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
        File.WriteAllBytes(_filePath, OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : plain);
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
