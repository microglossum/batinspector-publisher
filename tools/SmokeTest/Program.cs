// Manual smoke test host for BatInspectorPublisher (a NuGet package cannot be run on its own).
// Credentials come from appsettings.local.json (git-ignored; copy appsettings.example.json). Never commit them.
// Usage (run from tools/SmokeTest):
//   dotnet run -- login                       interactive browser login, stores the token
//   dotnet run -- dry-run <input.json>        resolve + duplicate check, writes nothing
//   dotnet run -- publish <input.json>        REAL, public, irreversible; asks for confirmation
// Keep real observation data (input files, evidence) under local/, which is git-ignored.
using System.Diagnostics;
using System.Text.Json;
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.InputSchema;
using BatInspectorPublisher.Core.Results;

var command = args.Length > 0 ? args[0] : "";
var settings = LoadSettings();

var options = new INaturalistOptions
{
    ClientId = settings.ClientId,
    ClientSecret = string.IsNullOrWhiteSpace(settings.ClientSecret) ? null : settings.ClientSecret,
};

using var http = new HttpClient();

// DPAPI only exists on Windows; on Linux/macOS the dev token is kept in plaintext (smoke test only).
INaturalistTokenStore store = new ProtectedFileTokenStore(allowPlaintextOnNonWindows: !OperatingSystem.IsWindows());

var auth = new INaturalistAuthenticator(options, http, store, prompt: ShowLoginUrl);

switch (command)
{
    case "login":
        var token = await auth.LoginAsync();
        Console.WriteLine($"Logged in as {token.Username}.");
        return 0;

    case "dry-run":
    case "publish":
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Missing input file.");
            return 2;
        }

        var commit = command == "publish";
        if (commit && !Confirm("This creates PUBLIC iNaturalist observations and cannot be undone. Type 'yes' to continue: "))
        {
            return 1;
        }

        var doc = InputSchemaReader.ReadFile(args[1]);
        var publisher = new INaturalistPublisher(options, http, auth);
        var progress = new Progress<PublishResult>(r =>
            Console.WriteLine($"{r.Status,-24} {r.Candidate.ScientificName} {r.ObservationId} {r.Url} {r.Message}"));
        var results = await new ExportOrchestrator(publisher).RunAsync(doc.Candidates, new PublishOptions { Commit = commit }, progress);
        Console.WriteLine($"{results.Count} candidate(s) processed, commit={commit}.");
        return 0;

    default:
        Console.Error.WriteLine("Usage: login | dry-run <input.json> | publish <input.json>");
        return 2;
}

static Task ShowLoginUrl(Uri url, CancellationToken ct)
{
    // Print the URL (works in a devcontainer where no browser can be launched) and try to open it as well.
    Console.WriteLine($"Open this URL to authorize:\n{url}\n");
    try
    {
        Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
    }
    catch
    {
        // No browser available here: the printed URL is enough.
    }

    return Task.CompletedTask;
}

static bool Confirm(string question)
{
    Console.Write(question);
    return string.Equals(Console.ReadLine()?.Trim(), "yes", StringComparison.Ordinal);
}

static (string ClientId, string? ClientSecret) LoadSettings()
{
    const string FileName = "appsettings.local.json";
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            var id = root.TryGetProperty("OAuthClientId", out var i) ? i.GetString() : null;
            var secret = root.TryGetProperty("OAuthClientSecret", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith("PASTE_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Set OAuthClientId in {path}.");
            }

            return (id, secret);
        }
    }

    throw new FileNotFoundException($"{FileName} not found (looked upwards from the working directory).");
}
