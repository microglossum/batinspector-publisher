namespace BatInspectorPublisher.Adapters.Naturgucker;

/// <summary>
/// The platform answered in a way the adapter cannot use: an HTTP error or an answer that is not understood. The message
/// never contains the request (it carries the login), only what is needed to find the cause.
/// </summary>
internal sealed class NaturguckerApiException(string message, Exception? innerException = null) : Exception(message, innerException);
