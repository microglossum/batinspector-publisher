using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Stores the token in a file. On Windows it is DPAPI-encrypted for the current user. DPAPI does
/// not exist elsewhere, and silently writing the OAuth access token, which never expires, as plain text
/// is not acceptable for a public package, so on other platforms saving throws <see cref="PlatformNotSupportedException"/>
/// unless <c>allowPlaintextOnNonWindows</c> is set explicitly.
/// <para>
/// The file is written to a temporary file first and then moved over the old one, so an interrupted
/// write never leaves a half-written token. On Linux and macOS it is created with mode <c>0600</c>
/// (owner read/write only). That protects against other local users, not against other processes of the same user.
/// </para>
/// <para>An unreadable file (corrupt, or encrypted by another Windows user) is treated as "no token" and logged as a warning.</para>
/// </summary>
public sealed partial class ProtectedFileTokenStore : INaturalistTokenStore
{
    [LoggerMessage(EventId = 2201, EventName = "TokenFileUnreadable", Level = LogLevel.Warning,
        Message = "The stored iNaturalist token in {Path} cannot be read and is ignored; a new login is needed")]
    private static partial void LogUnreadable(ILogger logger, Exception error, string path);

    private readonly string _filePath;
    private readonly bool _allowPlaintext;
    private readonly ILogger _logger;

    /// <summary>Uses <c>%LOCALAPPDATA%/BatInspectorPublisher/inaturalist/oauthtoken.dat</c> (or the platform equivalent).</summary>
    public ProtectedFileTokenStore(bool allowPlaintextOnNonWindows = false, ILogger? logger = null)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatInspectorPublisher", "inaturalist"), allowPlaintextOnNonWindows, logger)
    {
    }

    /// <summary>Uses <paramref name="directory"/> for the token file.</summary>
    public ProtectedFileTokenStore(string directory, bool allowPlaintextOnNonWindows = false, ILogger? logger = null)
    {
        _filePath = Path.Combine(directory, "oauthtoken.dat");
        _allowPlaintext = allowPlaintextOnNonWindows;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>True on Windows, and elsewhere only with <c>allowPlaintextOnNonWindows</c>.</summary>
    public bool CanSave => OperatingSystem.IsWindows() || _allowPlaintext;

    /// <inheritdoc />
    public INaturalistToken? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var stored = File.ReadAllBytes(_filePath);
        try
        {
            var plain = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(stored, optionalEntropy: null, DataProtectionScope.CurrentUser)
                : stored;
            return JsonSerializer.Deserialize<INaturalistToken>(Encoding.UTF8.GetString(plain))
                ?? throw new JsonException("The token file is empty.");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // The next Save replaces the file, so there is no need to delete it here.
            LogUnreadable(_logger, ex, _filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(INaturalistToken token)
    {
        if (!CanSave)
        {
            throw new PlatformNotSupportedException(
                "Encrypted token storage is only available on Windows. Pass allowPlaintextOnNonWindows: true to store the token unencrypted, or supply your own INaturalistTokenStore.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
        var content = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : plain;

        // Same directory as the target, so the move is a rename on one volume. The mode applies at
        // creation (not available on Windows), so the token is never readable by others, not even briefly.
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tempPath, options))
            {
                stream.Write(content);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
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
