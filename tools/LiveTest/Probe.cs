using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveTest;

/// <summary>
/// A host-side message handler around the HttpClient the package gets. It records every request (method, path, status, time,
/// rate-limit related headers and the SHAPE of a JSON answer: property names and kinds, not values), and can answer a request
/// with a fault before it leaves the machine. It never logs a request header, a request body, or any answer of the OAuth and
/// API-token endpoints, so no token can end up in the log.
/// </summary>
sealed partial class ProbeHandler : DelegatingHandler
{
    public sealed record Call(DateTimeOffset Start, string Method, string Target, int Status, long Millis, string Headers, string Shape);

    readonly object _lock = new();
    readonly List<Call> _calls = [];
    readonly string _logPath;

    public ProbeHandler(string logPath)
        : base(new HttpClientHandler())
    {
        _logPath = logPath;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    }

    /// <summary>Answers a request instead of sending it, or returns null to let it through.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Fault { get; set; }

    public bool Trace { get; set; }

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var uri = request.RequestUri!;
        var isApi = uri.Host == "api.inaturalist.org";
        // Query strings are logged for the API host only (search parameters); the www host carries the OAuth traffic.
        var target = isApi ? $"{uri.Host}{uri.PathAndQuery}" : $"{uri.Host}{uri.AbsolutePath}";

        HttpResponseMessage response;
        string injected = "";
        if (Fault?.Invoke(request) is { } fault)
        {
            response = fault;
            injected = " [INJECTED FAULT]";
        }
        else
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var shape = "";
        if (isApi && injected.Length == 0)
        {
            try
            {
                await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                shape = response.IsSuccessStatusCode ? Shape(body) : $"error body: {Truncate(body, 400)}";
            }
            catch (Exception e) when (e is JsonException or HttpRequestException or InvalidOperationException)
            {
                shape = $"(body not summarized: {e.GetType().Name})";
            }
        }

        var headers = string.Join(' ', response.Headers.Concat(response.Content.Headers)
            .Where(h => InterestingHeader().IsMatch(h.Key))
            .Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
        var call = new Call(start, request.Method.Method, target, (int)response.StatusCode, clock.ElapsedMilliseconds, headers, shape + injected);
        lock (_lock)
        {
            _calls.Add(call);
            File.AppendAllText(_logPath, $"{call.Start:HH:mm:ss.fff} {call.Method,-4} {call.Status} {call.Millis,5} ms {call.Target}  {call.Headers}  {call.Shape}\n", Encoding.UTF8);
        }

        if (Trace)
        {
            Console.WriteLine($"    http {call.Method,-4} {call.Status} {call.Millis,5} ms {call.Target} {call.Headers} {call.Shape}");
        }

        return response;
    }

    /// <summary>The smallest gap between the starts of two consecutive calls from <paramref name="index"/> on.</summary>
    public (TimeSpan? MinGap, int Count) PacingSince(int index)
    {
        var calls = Calls.Skip(index).ToList();
        TimeSpan? min = null;
        for (var i = 1; i < calls.Count; i++)
        {
            var gap = calls[i].Start - calls[i - 1].Start;
            if (min is null || gap < min)
            {
                min = gap;
            }
        }

        return (min, calls.Count);
    }

    /// <summary>All distinct rate-limit related headers seen so far.</summary>
    public IEnumerable<string> HeaderNamesSeen() =>
        Calls.SelectMany(c => c.Headers.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(h => h.Split('=')[0]).Distinct();

    [GeneratedRegex("rate|limit|retry|throttl|quota", RegexOptions.IgnoreCase)]
    private static partial Regex InterestingHeader();

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    /// <summary>Names and kinds of the properties of a JSON answer (the first element of "results" for a list answer).</summary>
    static string Shape(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var prefix = "";
        var element = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            var total = root.TryGetProperty("total_results", out var t) ? t.ToString() : "?";
            prefix = $"results={results.GetArrayLength()} total={total} first: ";
            if (results.GetArrayLength() == 0)
            {
                return prefix + "(none)";
            }

            element = results[0];
        }

        return prefix + Describe(element, 0);
    }

    static string Describe(JsonElement e, int depth)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var parts = e.EnumerateObject().Select(p =>
                    depth == 0 && p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? $"{p.Name}:{Describe(p.Value, depth + 1)}"
                        : $"{p.Name}:{Kind(p.Value)}");
                return "{" + string.Join(' ', parts) + "}";
            case JsonValueKind.Array:
                return e.GetArrayLength() == 0 ? "[]" : $"[{e.GetArrayLength()}x {Kind(e[0])}]";
            default:
                return Kind(e);
        }
    }

    static string Kind(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "obj",
        JsonValueKind.Array => e.GetArrayLength() == 0 ? "[]" : $"[{Kind(e[0])}]",
        JsonValueKind.String => "str",
        JsonValueKind.Number => "num",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Null => "null",
        _ => "?",
    };
}
