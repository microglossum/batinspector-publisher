using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// What the host passes to the naturgucker adapter. The package ships no credentials and no app identifier, reads no
/// config file or environment variable, and stores nothing: the login travels with every request, so the password is as
/// good as a token and is handled like one (never logged, never part of <see cref="ToString"/>, never written to disk).
/// </summary>
internal sealed class NaturguckerOptions
{
    /// <summary>The platform's service number of the host app, issued by the platform per app. Required.</summary>
    public long ServiceNumber { get; init; }

    /// <summary>E-mail address of the user's naturgucker account. Required.</summary>
    public string Email { get; init; } = "";

    /// <summary>Password of the user's naturgucker account. Required; only its hash leaves the process.</summary>
    public string Password { get; init; } = "";

    /// <summary>
    /// Asks the platform to hide the place and the observer of every observation (its one privacy switch). Default false.
    /// The platform has no finer levels, so there is nothing to map from other values.
    /// </summary>
    public bool LockPlaceAndObserver { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> when something required is missing; called before any request.</summary>
    public void ValidateForPublishing()
    {
        if (ServiceNumber == 0)
        {
            throw new ArgumentException("The service number is required: the platform issues one per app.", nameof(ServiceNumber));
        }

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("The e-mail address of the naturgucker account is required.", nameof(Email));
        }

        if (string.IsNullOrEmpty(Password))
        {
            throw new ArgumentException("The password of the naturgucker account is required.", nameof(Password));
        }
    }

    /// <summary>The form of the password the platform expects with every request: lower-case hex of the MD5 of its UTF-8 bytes.</summary>
    // The platform requires MD5 here; it is not our choice, and the request only ever goes over HTTPS.
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "The platform's login format.")]
    public string PasswordHash() => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Password))).ToLowerInvariant();

    /// <summary>Never contains the password.</summary>
    public override string ToString() => $"NaturguckerOptions {{ ServiceNumber = {ServiceNumber}, LockPlaceAndObserver = {LockPlaceAndObserver} }}";
}
