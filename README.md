# BatInspectorPublisher

[![CI](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml/badge.svg)](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/microglossum/batinspector-publisher/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)

.NET library that publishes bat acoustic-monitoring observations from
BatInspector to citizen-science platforms.
Supported today: **iNaturalist**. Planned: NABU|naturgucker (blocked until NABU documents an API).

Deutsche Version: [README.de.md](https://github.com/microglossum/batinspector-publisher/blob/main/README.de.md)

> Status: `0.x` preview. The public API may still change before 1.0.

## How it works

```text
BatInspector export (JSON) ──► InputSchemaReader ──► ObservationCandidate[]
                                                          │
                                         ExportOrchestrator ──► IObservationPublisher
                                                                   └─ INaturalistPublisher
```

* One observation per `DocumentFiles` entry: species, time, location, spectrogram (photo) and audio (sound).
  Nothing is published without both evidence files.
* Every adapter owns its own authentication, species resolution, duplicate check and upload.
  Authentication is **not** OAuth-only; OAuth is just what iNaturalist uses.

Targets .NET 8 and .NET 10.

## Quick start (iNaturalist)

```csharp
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.InputSchema;

// Your own iNaturalist OAuth application - the package ships no credentials.
var options = new INaturalistOptions { ClientId = "...", ClientSecret = "..." };

using var http = new HttpClient();
var auth = new INaturalistAuthenticator(options, http);           // Windows: DPAPI-encrypted token store
var publisher = new INaturalistPublisher(options, http, auth);

// The dry run logs in too: without a stored token, the first run (dry or not) opens the browser.
// Log in explicitly before it instead, for example from a "connect to iNaturalist" button.
if (!auth.HasStoredToken)
    await auth.LoginAsync();

var input = InputSchemaReader.ReadFile(@"C:\path\to\export.json");
var orchestrator = new ExportOrchestrator(publisher);

// Default is a dry run: it goes through every step and writes nothing.
// The report holds the rejected entries, the warnings and one result per candidate.
var preview = await orchestrator.RunAsync(input, new PublishOptions());
foreach (var r in preview.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Message}");

// Publish for real, after the user has seen and confirmed the preview.
var report = await orchestrator.RunAsync(input, new PublishOptions { Commit = true });
foreach (var r in report.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Url}");
```

## Before you publish

Publishing is public and irreversible, so a run is a dry run unless you pass `Commit = true`. A dry run goes through every
step of a real run (login, species resolution, evidence checks, iNaturalist's rules, the duplicate check) and stops where
the first write would happen. `WouldCreate` and `WouldResume` say what a real run would do; every skipped status is the
same as in a real run, with the reason in `Message`. `PublishResult.Description` holds the (German) text that becomes the
public description.

* **Show `Rejected` and `Warnings` to the user and let them confirm before you call it with `Commit = true`.** Both exist
  as soon as the file is read. The `RunReport` returned by `RunAsync(InputDocument, ...)` carries them; a run over the
  candidates alone does not. A warning does not stop an entry from being published.
* A dry run cannot foresee duplicates within one file: several entries for the same species, night and place all say
  `WouldCreate`, and a real run skips all but the first.
* The upload itself is not tried in a dry run. The evidence is checked against iNaturalist's documented rules, but only a
  real run shows whether iNaturalist accepts it.
* Without a dry run in your application, publish in small batches: the first batch serves as the preview, and the
  duplicate check makes a repeated run safe.

## Credentials

The package contains **no** OAuth client ID or secret and reads no config files or environment variables. Register your
application once and pass the values via `INaturalistOptions`; iNaturalist requires the developer of the host application
(not its users) to be approved as an "App Owner" first. Steps: [docs/inaturalist-setup.md](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist-setup.md).

The login is kept by an `INaturalistTokenStore`; the default encrypts it with Windows DPAPI and refuses plain text elsewhere
unless you opt in. Details and custom stores: [Token storage](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist-setup.md#token-storage).

## Input format

The input is BatInspector's export file. Its schema is versioned: `SchemaVersion` is required, and newer versions than the
library supports are rejected. Only a structurally unusable file throws (`InputSchemaException`); a bad entry is left out
of `InputDocument.Candidates` and reported in `InputDocument.Rejected`, a doubtful one is published and reported in
`InputDocument.Warnings`. The file holds one reference recording per species, night and place, not every detection.

```json
{
  "SchemaVersion": 1,
  "DocumentFiles": [
    {
      "Date": "13.06.2026 04:26:34",
      "Latitude": 50.11,
      "Longitude": 8.682,
      "SpeciesLatin": "Pipistrellus nathusii",
      "SpeciesLocal": "Rauhautfledermaus",
      "Temperature": 20.5,
      "Humidity": 92.44,
      "Comment": null,
      "PathToPng": "C:\\data\\wav\\pnat_20260613_042634.png",
      "PathToWav": "C:\\data\\wav\\PNAT_20260613_042634.wav"
    }
  ]
}
```

Every field, time zones, species names and what is rejected or only warns: [docs/input-format.md](https://github.com/microglossum/batinspector-publisher/blob/main/docs/input-format.md).
How BatInspector's group values (`Nyctaloid`, `Mbart`, ...) are filed: [Species and taxon](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist.md#species-and-taxon).

## Results

`ExportOrchestrator.RunAsync(InputDocument, ...)` returns a `RunReport`: the document's `Rejected` entries and `Warnings`,
one `PublishResult` per candidate that was started (`WarningsFor(result)`, `CountByStatus`), and `IsComplete`.
`ObservationCandidate.EntryIndex` maps a result back to its entry. The statuses and what they mean:
[docs/inaturalist.md#results](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist.md#results).
What each iNaturalist field receives and what becomes public: [docs/inaturalist.md](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist.md);
every request sent to iNaturalist: [docs/inaturalist-api.md](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist-api.md).

**Cancelling** does not throw. The run stops and returns the results so far; the interrupted candidate is the last result,
with status `Cancelled`, and candidates not yet started get no result. `RunReport.IsComplete` is false for a cancelled run.
The overload that takes the candidates returns only the results: there, check your own token.

**Re-running after a partial result:** a `Failed` or `Cancelled` result may be partial and needs no cleanup. Run again: the
duplicate check skips what exists, and an incomplete observation this package created for the same entry gets its missing
evidence (`Resumed`). Nothing is ever deleted. Details: [Re-running: duplicates and resume](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist.md#re-running-duplicates-and-resume).

**Rate and retries:** requests are spaced one second apart (`INaturalistOptions.MinRequestInterval`). Failed requests are not
retried by default; `INaturalistOptions.MaxAttempts = 3` turns on retries, which only ever repeat what cannot create anything
twice. Details: [Request rate and retries](https://github.com/microglossum/batinspector-publisher/blob/main/docs/inaturalist-setup.md#request-rate-and-retries).

## Logging

The library logs through `Microsoft.Extensions.Logging.Abstractions`; every class that logs takes an optional `ILogger`, and nothing is written without one.
Debug logs the HTTP calls, Information the outcomes, Warning the fallbacks, Error a `Failed` candidate with its exception. Tokens are never logged.
Levels, event IDs and how to plug in a console or file logger: [docs/logging.md](https://github.com/microglossum/batinspector-publisher/blob/main/docs/logging.md).

## Development

```text
dotnet build
dotnet test
dotnet format
```

VS Code tasks for all of these are in `.vscode/tasks.json`. See [CONTRIBUTING.md](https://github.com/microglossum/batinspector-publisher/blob/main/CONTRIBUTING.md).
A complete sample host (login, dry run, publish with confirmation): [tools/SmokeTest/Program.cs](https://github.com/microglossum/batinspector-publisher/blob/main/tools/SmokeTest/Program.cs).
Documentation is kept in English and German (`*.md` / `*.de.md`).

## License

[MIT](https://github.com/microglossum/batinspector-publisher/blob/main/LICENSE)
