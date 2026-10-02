using System.Net;
using System.Text;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core.Models;

namespace BatInspectorPublisher.Tests.Adapters.INaturalist;

/// <summary>Hand-written HTTP stub: routes by "METHOD path" prefix, records every request. No live API calls in tests.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpHandler On(string methodAndPathPrefix, HttpStatusCode status, string body)
    {
        var (method, path) = (methodAndPathPrefix.Split(' ')[0], methodAndPathPrefix.Split(' ')[1]);
        _routes.Add((
            r => r.Method.Method == method && r.RequestUri!.PathAndQuery.StartsWith(path, StringComparison.Ordinal),
            _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, content));
        foreach (var (match, respond) in _routes)
        {
            if (match(request))
            {
                return respond(request);
            }
        }

        throw new InvalidOperationException($"No stub route for {request.Method} {request.RequestUri}");
    }
}

internal sealed record RecordedRequest(string Method, Uri Uri, string? BearerToken, string Body)
{
    public string PathAndQuery => Uri.PathAndQuery;
}

internal sealed class InMemoryTokenStore : INaturalistTokenStore
{
    public INaturalistToken? Token { get; set; }
    public INaturalistToken? Load() => Token;
    public void Save(INaturalistToken token) => Token = token;
    public void Clear() => Token = null;
}

internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only (DPAPI).";
        }
    }
}

internal static class TestData
{
    public static INaturalistOptions Options(string? redirectUri = null) => new()
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
        RedirectUri = redirectUri ?? "http://127.0.0.1:45679/callback",
    };

    public static ObservationCandidate Candidate(string? comment = null, string? spectrogram = null, string? audio = null) => new()
    {
        ScientificName = "Pipistrellus pipistrellus",
        LocalName = "Zwergfledermaus",
        ObservedAt = new DateTime(2026, 6, 11, 21, 37, 49),
        Latitude = 50.11,
        Longitude = 8.682,
        TemperatureCelsius = 18.944397,
        HumidityPercent = 73.5,
        Comment = comment,
        SpectrogramPath = spectrogram ?? "spectrogram.png",
        AudioPath = audio ?? "audio.wav",
    };

    public static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
