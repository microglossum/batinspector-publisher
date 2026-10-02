using System.Net;

namespace BatInspectorPublisher.Adapters.INaturalist;

/// <summary>An iNaturalist endpoint answered with a non-success status.</summary>
public sealed class INaturalistApiException : Exception
{
    /// <summary>HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Response body (may be empty).</summary>
    public string ResponseBody { get; }

    /// <summary>Creates the exception.</summary>
    public INaturalistApiException(string operation, HttpStatusCode statusCode, string responseBody)
        : base($"{operation} failed ({(int)statusCode} {statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
