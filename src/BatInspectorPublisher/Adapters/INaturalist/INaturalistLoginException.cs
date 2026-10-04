namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>Why the interactive iNaturalist login did not finish.</summary>
public enum INaturalistLoginFailure
{
    /// <summary>No loopback port could be opened for the redirect, see <see cref="INaturalistOptions.RedirectUri"/>.</summary>
    ListenerUnavailable,

    /// <summary>Nobody completed the browser login within <see cref="INaturalistOptions.AuthorizationTimeout"/>.</summary>
    TimedOut,

    /// <summary>The user declined, or iNaturalist answered the authorization request with an error.</summary>
    Denied,
}

/// <summary>
/// The interactive browser login failed before iNaturalist issued a token. A rejection by iNaturalist's
/// token endpoint or API is an <see cref="INaturalistApiException"/> instead; a login the caller cancels
/// is an <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class INaturalistLoginException : Exception
{
    /// <summary>What went wrong.</summary>
    public INaturalistLoginFailure Reason { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="reason">What went wrong.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public INaturalistLoginException(INaturalistLoginFailure reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }
}
