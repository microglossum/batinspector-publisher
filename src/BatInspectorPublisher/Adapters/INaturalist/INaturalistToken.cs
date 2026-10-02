namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// What is persisted locally after login. <see cref="AccessToken"/> is the API JWT (see
/// <see cref="INaturalistOptions.ApiTokenExchangeUrl"/>), not the raw OAuth access token.
/// </summary>
public sealed record INaturalistToken
{
    /// <summary>The JWT sent as Bearer token to the REST API.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// The raw OAuth access token. iNaturalist's OAuth access tokens do not expire, so this is what
    /// lets the library get a new <see cref="AccessToken"/> every 24 hours without a new browser login.
    /// </summary>
    public string? OAuthAccessToken { get; init; }

    /// <summary>OAuth refresh token, if iNaturalist issued one (it currently does not).</summary>
    public string? RefreshToken { get; init; }

    /// <summary>iNaturalist login of the authorized user.</summary>
    public string? Username { get; init; }

    /// <summary>When <see cref="AccessToken"/> was obtained.</summary>
    public DateTimeOffset ObtainedAtUtc { get; init; }

    /// <summary>Lifetime of <see cref="AccessToken"/> in seconds.</summary>
    public int ExpiresInSeconds { get; init; }

    /// <summary>Moment the token expires.</summary>
    public DateTimeOffset ExpiresAtUtc => ObtainedAtUtc.AddSeconds(ExpiresInSeconds);

    /// <summary>True once the token is within 5 minutes of expiry (or past it), so callers refresh proactively.</summary>
    public bool IsLikelyExpired(DateTimeOffset now) => now >= ExpiresAtUtc - TimeSpan.FromMinutes(5);
}
