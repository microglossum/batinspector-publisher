namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Configuration of the iNaturalist adapter. The package ships no credentials: the host
/// application registers its own OAuth application at https://www.inaturalist.org/oauth/applications/new
/// and passes the values in here (from its own settings, user secrets, environment, ...).
/// </summary>
public sealed class INaturalistOptions
{
    /// <summary>Client ID of the host application's registered iNaturalist OAuth application.</summary>
    public required string ClientId { get; init; }

    /// <summary>Client secret of a confidential OAuth application. Leave empty for a public (non-confidential) application.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Must match a redirect URI registered for the OAuth application exactly, including the port.
    /// The login listens on this port first. If it is in use, the login retries on OS-assigned loopback
    /// ports with the same host and path; iNaturalist ignores the port of a loopback redirect URI when
    /// matching, so the one registered URI is enough. The host must be 127.0.0.1,
    /// not localhost: HttpListener matches the request's Host header text verbatim.
    /// </summary>
    public string RedirectUri { get; init; } = "http://127.0.0.1:45679/callback";

    /// <summary>How long to wait for the user to finish the browser login.</summary>
    public TimeSpan AuthorizationTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Comma-separated tags added to every observation.</summary>
    public string TagList { get; init; } = "bat,acoustic-monitoring,batinspector";

    /// <summary>Text placed at the start of every observation description (German, as users are German speaking).</summary>
    public string DescriptionPrefix { get; init; } = "Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung).";

    /// <summary>
    /// Largest spectrogram or audio file the publisher uploads, in bytes (decimal: 20 MB = 20,000,000). A larger file
    /// is skipped as <c>SkippedInvalidEvidence</c> before the observation is created: iNaturalist rejects it at the
    /// upload, which would leave a public observation without its evidence. iNaturalist's limit is 20 MB per file
    /// according to its forum (not in its documentation; the unit is unknown, so this default is the safe reading).
    /// Raise it if iNaturalist changes the limit.
    /// </summary>
    public long MaxEvidenceBytes { get; init; } = 20_000_000;

    /// <summary>Radius in km for the "does this observation already exist" check.</summary>
    public double DuplicateCheckRadiusKm { get; init; } = 0.1;

    /// <summary>
    /// How often a request is tried in total, the first try included. The default is 1: no retry, so a transient failure ends the
    /// candidate as <c>Failed</c> and the next run completes it. Set 3 (or more) to retry. Only a failure that is safe to repeat is retried:
    /// every request is retried after a 429 (iNaturalist refused it before processing), a read request (taxon search,
    /// duplicate check, user profile, token exchange) also after a 500, 502, 503, 504, a network error or a timeout.
    /// A write request (create, photo, sound) is not repeated after those, because the server may already have
    /// processed it and a repeat could create a second observation; it ends as <c>Failed</c> and the next run completes it.
    /// A write is repeated after a failed connection setup, since nothing was sent then.
    /// </summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>Wait before the second try; each further retry waits twice as long as the one before, up to <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Longest wait before a retry. When iNaturalist sends a <c>Retry-After</c> header, that wait is used instead of the backoff;
    /// a header asking for more than this is not waited out, the request fails with that answer (see <see cref="INaturalistApiException.RetryAfter"/>).
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Minimum time between the starts of two requests of one <see cref="INaturalistPublisher"/> (or <see cref="INaturalistAuthenticator"/>).
    /// iNaturalist throttles at 100 requests per minute and asks clients to stay at 60 or fewer; the default of one second keeps a
    /// batch under that. <see cref="TimeSpan.Zero"/> turns pacing off.
    /// </summary>
    public TimeSpan MinRequestInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>iNaturalist API v1 base URL.</summary>
    public string ApiBaseUrlV1 { get; init; } = "https://api.inaturalist.org/v1";

    /// <summary>iNaturalist API v2 base URL.</summary>
    public string ApiBaseUrlV2 { get; init; } = "https://api.inaturalist.org/v2";

    /// <summary>OAuth authorize endpoint.</summary>
    public string OAuthAuthorizeUrl { get; init; } = "https://www.inaturalist.org/oauth/authorize";

    /// <summary>OAuth token endpoint.</summary>
    public string OAuthTokenUrl { get; init; } = "https://www.inaturalist.org/oauth/token";

    /// <summary>
    /// iNaturalist quirk (undocumented in the API reference): the REST API does not accept a raw
    /// OAuth access token as Bearer (401). The token must first be exchanged for a JWT here, using
    /// the OAuth token as Bearer. Note the host is www.inaturalist.org, not api.
    /// </summary>
    public string ApiTokenExchangeUrl { get; init; } = "https://www.inaturalist.org/users/api_token";

    internal void ValidateForRequests()
    {
        if (MaxAttempts < 1)
        {
            throw new ArgumentException("INaturalistOptions.MaxAttempts must be at least 1 (1 = no retry).", nameof(MaxAttempts));
        }

        if (RetryBaseDelay < TimeSpan.Zero || MaxRetryDelay < TimeSpan.Zero || MinRequestInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("INaturalistOptions.RetryBaseDelay, MaxRetryDelay and MinRequestInterval must not be negative.");
        }
    }

    internal void ValidateForOAuth()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new ArgumentException("INaturalistOptions.ClientId must be set to the Client ID of your registered iNaturalist OAuth application.", nameof(ClientId));
        }

        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("INaturalistOptions.RedirectUri must be an absolute http loopback URI, e.g. http://127.0.0.1:45679/callback.", nameof(RedirectUri));
        }
    }
}
