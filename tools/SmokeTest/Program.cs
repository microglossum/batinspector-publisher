// Manual smoke test host for BatInspectorPublisher (a NuGet package cannot be run on its own).
// Credentials come from appsettings.local.json (git-ignored; copy appsettings.example.json). Never commit them.
// Usage (run from tools/SmokeTest):
//   dotnet run -- login                       interactive browser login, stores the token
//   dotnet run -- whoami                      uses the stored token only, never opens a browser (tests the token store)
//   dotnet run -- renew                       ages the stored API token by 2 days, then renews it silently from the stored OAuth token
//   dotnet run -- dry-run <input.json>        full simulation (login, taxon, duplicate check), writes nothing
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

// whoami and renew must never start a login: a prompt that throws proves no browser was needed.
AuthorizationPrompt prompt = command is "whoami" or "renew"
    ? (_, _) => throw new InvalidOperationException("A browser login would be needed, but this command must not start one.")
    : ShowLoginUrl;

var auth = new INaturalistAuthenticator(options, http, store, prompt);

switch (command)
{
    case "login":
        var token = await auth.LoginAsync();
        Console.WriteLine($"Logged in as {token.Username}.");
        return 0;

    case "whoami":
        Console.WriteLine($"Stored token: {auth.HasStoredToken}, user: {auth.StoredUsername ?? "-"}");
        if (auth.HasStoredToken)
        {
            await auth.EnsureAuthenticatedAsync();
            Console.WriteLine("Authenticated without a browser login.");
        }

        return auth.HasStoredToken ? 0 : 1;

    case "renew":
        if (store.Load() is not { } stored)
        {
            Console.Error.WriteLine("No stored token; run login first.");
            return 1;
        }

        // Pretend the 24 h API JWT is old, so the silent OAuth-to-JWT exchange must run (no browser).
        store.Save(stored with { ObtainedAtUtc = DateTimeOffset.UtcNow.AddDays(-2) });
        await auth.EnsureAuthenticatedAsync();
        Console.WriteLine($"Renewed silently; stored token obtained at {store.Load()!.ObtainedAtUtc:u}.");
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
        var report = await new ExportOrchestrator(publisher).RunAsync(doc, new PublishOptions { Commit = commit }, progress);
        foreach (var rejected in report.Rejected)
        {
            Console.WriteLine($"{"Rejected",-24} entry {rejected.Index}: {string.Join("; ", rejected.Issues)}");
        }

        foreach (var warning in report.Warnings)
        {
            Console.WriteLine($"{"Warning",-24} entry {warning.Index}: {warning.Issue}");
        }

        if (!commit)
        {
            foreach (var r in report.Results.Where(r => r.Description is not null))
            {
                Console.WriteLine($"{"Description",-24} {r.Candidate.ScientificName}: {r.Description}");
            }
        }

        Console.WriteLine($"{report.Results.Count} candidate(s) processed, commit={commit}, complete={report.IsComplete}: " +
                          string.Join(", ", report.CountByStatus.Select(kv => $"{kv.Key}={kv.Value}")));
        return 0;

    default:
        Console.Error.WriteLine("Usage: login | whoami | renew | dry-run <input.json> | publish <input.json>");
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
