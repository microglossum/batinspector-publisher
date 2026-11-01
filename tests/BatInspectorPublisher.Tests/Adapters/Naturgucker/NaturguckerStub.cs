using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BatInspectorPublisher.Tests.Adapters.Naturgucker;

/// <summary>One request the stub saw: which call (the platform's "modul"), how it was sent and what was in it.</summary>
internal sealed record StubRequest(string Modul, string Method, Uri Uri, string Body);

/// <summary>
/// Hand-written HTTP stub of the platform: every call goes to one address, so it routes by the call's name ("modul"),
/// wherever the request carries it (address, form, JSON or multipart body). Records every request. No live calls in tests.
/// </summary>
internal sealed partial class NaturguckerStub : HttpMessageHandler
{
    private readonly Dictionary<string, Func<StubRequest, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<StubRequest> Requests { get; } = [];

    public IEnumerable<string> Moduls => Requests.Select(r => r.Modul);

    public NaturguckerStub On(string modul, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(modul, _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public NaturguckerStub On(string modul, Func<StubRequest, HttpResponseMessage> respond)
    {
        _routes[modul] = respond;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new StubRequest(Modul(request, body), request.Method.Method, request.RequestUri!, body);
        Requests.Add(recorded);
        return _routes.TryGetValue(recorded.Modul, out var respond)
            ? respond(recorded)
            : throw new InvalidOperationException($"No stub route for '{recorded.Modul}' ({request.Method} {request.RequestUri!.GetLeftPart(UriPartial.Path)})");
    }

    private static string Modul(HttpRequestMessage request, string body)
    {
        if (request.Method == HttpMethod.Get)
        {
            return System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["modul"] ?? "";
        }

        if (body.StartsWith('{'))
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("modul").GetString() ?? "";
        }

        var multipart = MultipartModul().Match(body);
        return multipart.Success ? multipart.Groups[1].Value : System.Web.HttpUtility.ParseQueryString(body)["modul"] ?? "";
    }

    [GeneratedRegex("name=\"?modul\"?\\r\\n(?:[^\\r\\n]*\\r\\n)*?\\r\\n([^\\r\\n]*)\\r\\n")]
    private static partial Regex MultipartModul();
}
