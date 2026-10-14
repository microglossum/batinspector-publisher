namespace BatInspectorPublisher.Adapters.INaturalist;

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
