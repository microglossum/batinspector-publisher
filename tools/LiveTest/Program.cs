// Black-box live test host for the iNaturalist adapter (naturgucker will get its own commands and scenarios later).
// References the PACKED package only (./pack.sh), so it sees what a consumer sees. See README.md.
//   dotnet run -c Release -- login [--scratch] [--occupy-port]
//   dotnet run -c Release -- whoami | logout
//   dotnet run -c Release -- run scenarios/inaturalist/<file>.json [--commit] [--fail sound|photo] [--license cc-by-nc] [--trace]
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.InputSchema;
using BatInspectorPublisher.Core.Results;
using LiveTest;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = Encoding.UTF8;
var command = args.FirstOrDefault() ?? "";
string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var has = (string name) => args.Contains(name);

var (clientId, secret) = LoadCredentials();
const string Tags = "bat,acoustic-monitoring,batinspector,batinspector-livetest";
var options = new INaturalistOptions
{
    ClientId = clientId,
    ClientSecret = secret,
    TagList = Tags,
    License = Opt("--license"),
    DescriptionPrefix = "TESTDATEN (werden gelöscht): Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung).",
};
using var logs = LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(has("--log") ? LogLevel.Information : LogLevel.Warning));
var probe = new ProbeHandler(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out", $"http-{DateTime.Now:yyyyMMdd-HHmmss}.log")) { Trace = has("--trace") };
using var http = new HttpClient(probe) { Timeout = TimeSpan.FromMinutes(10) }; // a host passes a larger timeout for 20 MB uploads
var store = new ProtectedFileTokenStore(
    has("--scratch") ? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out", "scratch-token") : DefaultDir(),
    allowPlaintextOnNonWindows: !OperatingSystem.IsWindows());
var auth = new INaturalistAuthenticator(options, http, store, command == "login" ? ShowUrl : NoBrowser, logs.CreateLogger<INaturalistAuthenticator>());

switch (command)
{
    case "login":
        TcpListener? blocker = null;
        if (has("--occupy-port"))
        {
            blocker = new TcpListener(IPAddress.Loopback, new Uri(options.RedirectUri).Port);
            blocker.Start();
            Console.WriteLine("The redirect port is occupied on purpose; the login must fall back to another port.");
        }

        var clock = Stopwatch.StartNew();
        try
        {
            Console.WriteLine($"Logged in as {await auth.LoginAsync()} after {clock.Elapsed.TotalSeconds:F1} s.");
            return 0;
        }
        catch (INaturalistLoginException e)
        {
            Console.WriteLine($"Login ended after {clock.Elapsed.TotalSeconds:F1} s: Reason={e.Reason}: {e.Message}");
            return 1;
        }
        finally
        {
            blocker?.Stop();
        }

    case "whoami":
        Console.WriteLine($"Stored login: {auth.HasStoredToken}, user: {auth.StoredUsername ?? "-"}");
        return auth.HasStoredToken ? 0 : 1;

    case "logout":
        auth.Logout();
        Console.WriteLine("Stored login removed.");
        return 0;

    case "run" when args.Length > 1:
        return await RunAsync(args[1], has("--commit"), Opt("--fail"));

    default:
        Console.Error.WriteLine("Usage: login | whoami | logout | run <scenarios/inaturalist/file.json> [--commit] [--fail sound|photo]");
        return 2;
}

async Task<int> RunAsync(string file, bool commit, string? fail)
{
    if (!auth.HasStoredToken)
    {
        Console.Error.WriteLine("No stored login; run 'login' first.");
        return 2;
    }

    var doc = InputSchemaReader.ReadFile(file);
    foreach (var r in doc.Rejected)
    {
        Console.WriteLine($"Rejected  #{r.Index}: {string.Join("; ", r.Issues)}");
    }

    foreach (var w in doc.Warnings)
    {
        Console.WriteLine($"Warning   #{w.Index}: {w.Issue}");
    }

    // --fail: this host answers the upload itself with a 500 (nothing is sent), to leave a half-created observation behind.
    var part = fail is null ? null : fail == "sound" ? "observation_sounds" : "observation_photos";
    probe.Fault = part is null ? null : r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains(part, StringComparison.Ordinal)
        ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("injected by LiveTest, not sent") }
        : null;

    var publisher = new INaturalistPublisher(options, http, auth, logs.CreateLogger<INaturalistPublisher>());
    var report = await new ExportOrchestrator(publisher, logs.CreateLogger<ExportOrchestrator>())
        .RunAsync(doc, new PublishOptions { Commit = commit }, new Sync(r => Console.WriteLine(
            $"{r.Status,-24} #{r.Candidate.EntryIndex} {r.Candidate.ScientificName} {r.ObservationId} {r.Url}{(r.Message is null ? "" : $"\n{"",26}{r.Message}")}")));

    Console.WriteLine($"\n{report.Results.Count} result(s), commit={commit}, complete={report.IsComplete}: {string.Join(", ", report.CountByStatus.Select(kv => $"{kv.Key}={kv.Value}"))}");
    if (!commit && report.Results.FirstOrDefault(r => r.Description is not null) is { } first)
    {
        Console.WriteLine($"Description of #{first.Candidate.EntryIndex}: {first.Description}");
    }

    foreach (var r in report.Results.Where(r => r.Status is PublishStatus.Created or PublishStatus.Resumed or PublishStatus.Failed && r.ObservationId is not null))
    {
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out", "ledger.txt"), $"{DateTime.Now:s} {Path.GetFileName(file)} {r.Status} {r.ObservationId} {r.Url}\n");
        await ReadBackAsync(r.ObservationId!);
    }

    var (gap, calls) = probe.PacingSince(0);
    Console.WriteLine($"{calls} http call(s); shortest gap between request starts {gap?.TotalMilliseconds:F0} ms; rate-limit headers seen: {string.Join(", ", probe.HeaderNamesSeen().DefaultIfEmpty("none"))}");
    return 0;
}

// What a visitor gets: the observation through the public API, without a login.
async Task ReadBackAsync(string id)
{
    using var plain = new HttpClient();
    plain.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BatInspectorPublisher-LiveTest", "0.1"));
    var url = id.All(char.IsAsciiDigit) ? $"https://api.inaturalist.org/v1/observations/{id}" : $"https://api.inaturalist.org/v1/observations?uuid={id}";
    for (var attempt = 1; attempt <= 10; attempt++)
    {
        await Task.Delay(attempt == 1 ? 2000 : 3000);
        using var doc = JsonDocument.Parse(await plain.GetStringAsync(url));
        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() > 0)
        {
            var o = results[0];
            string S(string n) => o.TryGetProperty(n, out var v) ? v.ToString() : "-";
            var taxon = o.TryGetProperty("taxon", out var t) && t.ValueKind == JsonValueKind.Object ? $"{t.GetProperty("name")} ({t.GetProperty("rank")})" : "-";
            Console.WriteLine($"  read back #{S("id")}: taxon={taxon} species_guess='{S("species_guess")}' observed={S("time_observed_at")} zone={S("observed_time_zone")}/{S("time_zone")}");
            Console.WriteLine($"    place_guess='{S("place_guess")}' location={S("location")} geoprivacy={S("geoprivacy")} photos={o.GetProperty("photos").GetArrayLength()} sounds={o.GetProperty("sounds").GetArrayLength()}");
            string Lic(string list) => string.Join("/", o.GetProperty(list).EnumerateArray().Select(x => x.TryGetProperty("license_code", out var l) ? l.ToString() : "-"));
            Console.WriteLine($"    licenses: observation={S("license_code")} photo={Lic("photos")} sound={Lic("sounds")}");
            Console.WriteLine($"    tags=[{string.Join(", ", o.GetProperty("tags").EnumerateArray())}] (after {attempt} try/tries)");
            return;
        }
    }

    Console.WriteLine($"  #{id} is not visible in the public API yet");
}

static string DefaultDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatInspectorPublisher", "inaturalist");

static Task NoBrowser(Uri url, CancellationToken ct) => throw new InvalidOperationException("A browser login would be needed; run 'login' first.");

static Task ShowUrl(Uri url, CancellationToken ct)
{
    Console.WriteLine($"\nOpen this URL in your browser and authorize:\n{url}\n");
    try
    {
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    }
    catch
    {
        // No browser in the container: the printed URL is enough.
    }

    return Task.CompletedTask;
}

// The Client ID/secret live in the git-ignored appsettings.local.json (here or in tools/SmokeTest).
static (string Id, string? Secret) LoadCredentials()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        foreach (var path in new[] { Path.Combine(dir.FullName, "appsettings.local.json"), Path.Combine(dir.FullName, "SmokeTest", "appsettings.local.json") })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var id = json.RootElement.GetProperty("OAuthClientId").GetString()!;
            var secret = json.RootElement.TryGetProperty("OAuthClientSecret", out var s) ? s.GetString() : null;
            return (id, string.IsNullOrWhiteSpace(secret) ? null : secret);
        }
    }

    throw new FileNotFoundException("appsettings.local.json not found in tools/LiveTest or tools/SmokeTest.");
}

sealed class Sync(Action<PublishResult> action) : IProgress<PublishResult>
{
    public void Report(PublishResult value) => action(value);
}
