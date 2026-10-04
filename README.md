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
* Nothing is published without both evidence files. Each file is read once, checked (not empty, PNG / WAV signature) and the checked bytes are what gets uploaded.
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
  Windows DPAPI and refuses to write plain text on other platforms unless you opt in
  (`allowPlaintextOnNonWindows: true`). The plaintext file is created with mode `0600` and written atomically,
  which protects against other local users only, not against other processes of the same user. For real secure
  storage on Linux or macOS, implement `INaturalistTokenStore` on top of the OS keychain. A store that cannot save
  (`CanSave` is false) makes the login fail before the browser opens. An unreadable token file counts as "no token".
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
* Validation is per entry. A file that is not well-formed JSON, or has a missing or unsupported `SchemaVersion` or no `DocumentFiles` array, is rejected as a whole (`InputSchemaException`). A bad entry is left out of `InputDocument.Candidates` and listed with every problem in `InputDocument.Rejected`; the valid entries are still returned and can be published. Rejected entries are never published. Show `Rejected` to the user: the publish run does not report them. After fixing the file, run it again: entries that are already published are skipped as duplicates.
* Entry errors: evidence paths must be absolute (Windows or Unix syntax) and end in `.png` / `.wav`; `Latitude` and `Longitude` both `0` (missing GPS) is rejected; a `Date` in the future (compared with current German time) is rejected.
* The file is expected to hold one **reference recording** (*Referenzaufnahme*) per species, night and location, not every detection: the goal is to document that a species was present at a place and time, and one good recording is usually enough. The iNaturalist duplicate check (same taxon, same calendar day, within a radius of 100 m by default) skips a second entry for the same combination.
* `SchemaVersion` is required. Newer versions than the library supports are rejected.
* Required: `Date`, `Latitude`, `Longitude`, `SpeciesLatin`, `PathToPng`, `PathToWav`.
  Optional: `SpeciesLocal`, `Temperature`, `Humidity`, `Comment`, `TimeZone`. Unknown properties are ignored.
* `Date` (`dd.MM.yyyy HH:mm:ss`) carries no offset. It is read in the zone given by the optional `TimeZone` (an IANA id such as `Europe/Lisbon`; an unknown id rejects the entry), or as **German local time (Europe/Berlin)** when `TimeZone` is absent, including daylight saving time. It is stored with the offset of that zone on that date (`ObservationCandidate.ObservedAt` is a `DateTimeOffset`). This is never converted from the host's or the machine's time zone. A time that does not exist (the hour skipped when the clocks go forward) rejects the entry; a time in the repeated hour when the clocks go back is read as standard time and reported in `InputDocument.Warnings` (the entry is still published). An offset in the value (`Z`, `+02:00`) is a format error: name the zone with `TimeZone` instead. The duplicate check and the date sent to iNaturalist use the local calendar day of that zone. The future-date check compares instants. The host needs the time zone data for Europe/Berlin (the reader throws `TimeZoneNotFoundException` without it). iNaturalist receives only the calendar date, not the time.
* Species names are normalized (`Eptesicus Serotinus` becomes `Eptesicus serotinus`). A two-word name must match an active iNaturalist species exactly, a one-word name an active genus (`Myotis`).
  A name that matches nothing, matches only a synonym, has the wrong rank or is ambiguous is skipped with the reason in `PublishResult.Message`, never guessed. A name with three or more words (`Myotis cf. daubentonii`) is skipped too.
* BatInspector's group and uncertain values are the one exception: `Nyctaloid`, `Social` and `?` are filed under the order Chiroptera, `Mbart` under the genus Myotis. The original value stays as the observation's species guess and is named in the (German) description; `PublishResult.TaxonName` reports the taxon used. The user can refine it on iNaturalist.
  Other values that are no taxon name (`todo`, an unknown BatInspector code, a typo) are skipped, so a corrected re-run does not create a second observation.

BatInspector is open source ([chrmue44/BatInspector](https://github.com/chrmue44/BatInspector)); its code is the reference for what a field or value means (for example the species list in `BatInfo.cs`). The export and its newest values may live on another branch than `main` or not be pushed yet, so the public code can lag behind what BatInspector actually writes.

## Results

`ExportOrchestrator.RunAsync` returns one `PublishResult` per candidate with a `PublishStatus`:
`Created`, `WouldCreate` (dry run), `SkippedDuplicate`, `SkippedUnresolvedTaxon`,
`SkippedMissingEvidence`, `SkippedInvalidEvidence` (unreadable, empty, not a PNG / WAV file, or larger than the platform accepts), `SkippedInvalidEntry` (the entry breaks a rule of this platform, such as a date or position it rejects), `Resumed`, `Failed`, `Cancelled`.

A failed or cancelled result may be partial (observation created, evidence incomplete): then `ObservationId` is set, `SpectrogramAttached` / `AudioAttached` say what is there,
and `InterruptedStep` names the stage that stopped. If the stop came while the observation was being created, its existence is unknown; the next run finds out.

**Cancelling** does not throw. `RunAsync` stops and returns the results so far; the candidate that was interrupted is the last result, with status `Cancelled`. Candidates not yet started get no result. Check your own token to tell a cancelled run from a finished one.

**Re-running after a partial result:** the duplicate check normally skips an observation that already exists (`SkippedDuplicate`). If the existing observation is one this package created for the same entry
(identical description) and it still lacks the spectrogram or the audio, the run attaches the missing evidence instead and reports `Resumed`. Any other observation of yours is left untouched. Nothing is ever deleted.

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
