namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Why the interactive iNaturalist login did not finish.</summary>
public enum INaturalistLoginFailure
{
    /// <summary>No loopback port could be opened for the redirect, see <see cref="INaturalistOptions.RedirectUri"/>.</summary>
    ListenerUnavailable = 0,

    /// <summary>Nobody completed the browser login within <see cref="INaturalistOptions.AuthorizationTimeout"/>.</summary>
    TimedOut = 1,

    /// <summary>The user declined, or iNaturalist answered the authorization request with an error.</summary>
    Denied = 2,
}
