# BatInspectorPublisher

[![CI](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml/badge.svg)](https://github.com/microglossum/batinspector-publisher/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)

.NET-Bibliothek, die Fledermaus-Beobachtungen aus der akustischen Erfassung von
BatInspector auf Citizen-Science-Plattformen veröffentlicht.
Heute unterstützt: **iNaturalist**. Geplant: NABU|naturgucker (blockiert, bis NABU eine API dokumentiert).

English version: [README.md](README.md)

> Status: `0.x`-Vorschau. Die öffentliche API kann sich bis 1.0 noch ändern.

## Funktionsweise

```text
BatInspector-Export (JSON) ──► InputSchemaReader ──► ObservationCandidate[]
                                                          │
                                         ExportOrchestrator ──► IObservationPublisher
                                                                   └─ INaturalistPublisher
```

* Eine Beobachtung pro Eintrag in `DocumentFiles`: Art, Zeitpunkt, Ort, Spektrogramm (Foto) und Audio (Ton).
* Ohne beide Belegdateien wird nichts veröffentlicht.
* Jeder Adapter verantwortet Anmeldung, Artauflösung, Duplikatprüfung und Upload selbst.
  Die Anmeldung ist **nicht** überall OAuth; OAuth ist nur das Verfahren von iNaturalist.

Zielt auf .NET 8 und .NET 10.

## Schnellstart (iNaturalist)

```csharp
using BatInspectorPublisher.Adapters.INaturalist;
using BatInspectorPublisher.Core;
using BatInspectorPublisher.Core.InputSchema;

// Eigene iNaturalist-OAuth-Anwendung - das Paket enthält keine Zugangsdaten.
var options = new INaturalistOptions { ClientId = "...", ClientSecret = "..." };

using var http = new HttpClient();
var auth = new INaturalistAuthenticator(options, http);           // Windows: Token per DPAPI verschlüsselt
var publisher = new INaturalistPublisher(options, http, auth);

var input = InputSchemaReader.ReadFile(@"C:\pfad\zu\export.json");
var orchestrator = new ExportOrchestrator(publisher);

// Standard ist ein Trockenlauf: Arten werden aufgelöst, es wird nichts geschrieben.
var vorschau = await orchestrator.RunAsync(input.Candidates, new PublishOptions());

// Wirklich veröffentlichen (der erste Aufruf öffnet den Browser zur iNaturalist-Anmeldung).
var ergebnisse = await orchestrator.RunAsync(input.Candidates, new PublishOptions { Commit = true });
foreach (var r in ergebnisse)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Url}");
```

## Zugangsdaten

Das Paket enthält **keine** OAuth-Client-ID und kein Secret. Die Anwendung einmal registrieren
(siehe [docs/inaturalist-setup.de.md](docs/inaturalist-setup.de.md)) und die Werte über
`INaturalistOptions` aus den eigenen Einstellungen übergeben. Das Paket liest keine
Konfigurationsdateien und keine Umgebungsvariablen.

> **Hinweis:** iNaturalist lässt nicht jeden eine Anwendung anlegen. Man muss zuerst als "App Owner" zugelassen werden (Konto mindestens
> 2 Monate alt und 10 "improving identifications" im letzten Monat). Das braucht nur die Entwicklerin oder der Entwickler der Host-Anwendung, nicht deren Nutzer.
> Details: [docs/inaturalist-setup.de.md](docs/inaturalist-setup.de.md).

* Eine vertrauliche Anwendung nutzt `ClientSecret`; bei einer öffentlichen (nicht vertraulichen) Anwendung bleibt es leer.
* Tokens speichert ein `INaturalistTokenStore`. Der Standard `ProtectedFileTokenStore` verschlüsselt mit
  Windows-DPAPI und schreibt auf anderen Plattformen keinen Klartext, außer man stimmt ausdrücklich zu.
* Die Anmelde-URL wird über den Callback `AuthorizationPrompt` angezeigt; standardmäßig wird der Systembrowser geöffnet.

## Eingabeformat

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

* Das JSON muss nicht aus einer Datei kommen: `InputSchemaReader.Parse(string json)` und `InputSchemaReader.Read(Stream)` funktionieren wie `ReadFile`. Die Belegpfade darin müssen weiterhin auf Dateien auf dem Datenträger zeigen.
* `SchemaVersion` ist Pflicht. Neuere Versionen als die der Bibliothek werden abgelehnt.
* Pflicht: `Date`, `Latitude`, `Longitude`, `SpeciesLatin`, `PathToPng`, `PathToWav`.
  Optional: `SpeciesLocal`, `Temperature`, `Humidity`, `Comment`. Unbekannte Eigenschaften werden ignoriert.
* `Date` ist deutsche Ortszeit (`dd.MM.yyyy HH:mm:ss`, ohne Zeitzone).
* Artnamen werden normalisiert (`Eptesicus Serotinus` wird zu `Eptesicus serotinus`). Ein Name, den
  iNaturalist nicht exakt findet, wird übersprungen und nie geraten.

## Ergebnisse

`ExportOrchestrator.RunAsync` liefert pro Kandidat ein `PublishResult` mit einem `PublishStatus`:
`Created`, `WouldCreate` (Trockenlauf), `SkippedDuplicate`, `SkippedUnresolvedTaxon`,
`SkippedMissingEvidence`, `Failed`. Ein fehlgeschlagenes Ergebnis kann teilweise erfolgt sein
(Beobachtung angelegt, Belege unvollständig); dann ist `ObservationId` gesetzt.

## Entwicklung

```text
dotnet build
dotnet test
dotnet format
```

VS-Code-Tasks dafür stehen in `.vscode/tasks.json`. Konventionen: [CLAUDE.md](CLAUDE.md).
Die Dokumentation wird auf Englisch und Deutsch gepflegt (`*.md` / `*.de.md`).

## Lizenz

[MIT](LICENSE)
