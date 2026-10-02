# BatInspectorPublisher

[![CI](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml/badge.svg)](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
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
* Nothing is published without both evidence files.
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

var input = InputSchemaReader.ReadFile(@"C:\path\to\export.json");
var orchestrator = new ExportOrchestrator(publisher);

// Default is a dry run: resolves species, writes nothing.
var preview = await orchestrator.RunAsync(input.Candidates, new PublishOptions());

// Publish for real (first call opens the browser for the iNaturalist login).
var results = await orchestrator.RunAsync(input.Candidates, new PublishOptions { Commit = true });
foreach (var r in results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Url}");
```

## Credentials

The package contains **no** OAuth client ID or secret. Register your application once
(see [docs/inaturalist-setup.md](docs/inaturalist-setup.md)) and pass the values via
`INaturalistOptions` from wherever your application keeps its settings. The package reads no
config files and no environment variables.

> **Heads-up:** iNaturalist does not let everybody create an application. You must be approved as an "App Owner" first (account at least
> 2 months old and 10 improving identifications in the last month). Only the developer of the host application needs this, its users do not.
> Details: [docs/inaturalist-setup.md](docs/inaturalist-setup.md).

* A confidential application uses `ClientSecret`; a public (non-confidential) application leaves it empty.
* Tokens are stored by an `INaturalistTokenStore`. The default `ProtectedFileTokenStore` encrypts with
  Windows DPAPI and refuses to write plain text on other platforms unless you opt in.
* The login URL is shown through an `AuthorizationPrompt` callback; by default the system browser is opened.

## Input format

```json
{
  "SchemaVersion": 1,
  "DocumentFiles": [
    {
      "Date": "13.06.2026 04:26:34",
      "Latitude": 50.11, "Longitude": 8.682,
      "SpeciesLatin": "Pipistrellus nathusii", "SpeciesLocal": "Rauhautfledermaus",
      "Temperature": 20.05, "Humidity": 92.44,
      "Comment": null,
      "PathToPng": "C:\\data\\wav\\pnat_20260613_042634.png",
      "PathToWav": "C:\\data\\wav\\PNAT_20260613_042634.wav"
    }
  ]
}
```

* The JSON does not have to come from a file: `InputSchemaReader.Parse(string json)` and `InputSchemaReader.Read(Stream)` work the same as `ReadFile`. The evidence paths inside it must still point to files on disk.
* `SchemaVersion` is required. Newer versions than the library supports are rejected.
* Required: `Date`, `Latitude`, `Longitude`, `SpeciesLatin`, `PathToPng`, `PathToWav`.
  Optional: `SpeciesLocal`, `Temperature`, `Humidity`, `Comment`. Unknown properties are ignored.
* `Date` is German local time (`dd.MM.yyyy HH:mm:ss`, no time zone).
* Species names are normalized (`Eptesicus Serotinus` becomes `Eptesicus serotinus`). A name iNaturalist
  cannot match exactly is skipped, never guessed.

## Results

`ExportOrchestrator.RunAsync` returns one `PublishResult` per candidate with a `PublishStatus`:
`Created`, `WouldCreate` (dry run), `SkippedDuplicate`, `SkippedUnresolvedTaxon`,
`SkippedMissingEvidence`, `Failed`. A failed result may be partial (observation created, evidence incomplete);
then `ObservationId` is set.

## Development

```text
dotnet build
dotnet test
dotnet format
```

VS Code tasks for all of these are in `.vscode/tasks.json`. See [CLAUDE.md](CLAUDE.md) for conventions.
Documentation is kept in English and German (`*.md` / `*.de.md`).

## License

[MIT](LICENSE)
