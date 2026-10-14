namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>
/// Shows the authorization URL to the user. The default implementation opens the system browser;
/// a host application can pass its own to present the URL in its UI.
/// </summary>
/// <param name="authorizeUrl">The iNaturalist authorization URL the user must open.</param>
/// <param name="ct">Cancellation token.</param>
public delegate Task AuthorizationPrompt(Uri authorizeUrl, CancellationToken ct);
