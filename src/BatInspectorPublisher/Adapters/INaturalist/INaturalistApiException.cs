using System.Net;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>An iNaturalist endpoint answered with a non-success status.</summary>
public sealed class INaturalistApiException : Exception
{
    private const int MaxMessageBodyLength = 1000;

    /// <summary>HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The complete response body (may be empty). <see cref="Exception.Message"/> carries only its first 1000 characters.</summary>
    public string ResponseBody { get; }

    /// <summary>How long the server asked to wait before another request (<c>Retry-After</c> header), or null if it sent none.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Creates the exception.</summary>
    public INaturalistApiException(string operation, HttpStatusCode statusCode, string responseBody, TimeSpan? retryAfter = null)
        : base($"{operation} failed ({(int)statusCode} {statusCode}): {Shorten(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    // A 5xx can answer with a whole HTML page; the message ends up in logs and in PublishResult.Message.
    private static string Shorten(string body) =>
        body.Length > MaxMessageBodyLength ? body[..MaxMessageBodyLength] + "... (truncated)" : body;
}
