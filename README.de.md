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
* Ohne beide Belegdateien wird nichts veröffentlicht. Jede Datei wird einmal gelesen und geprüft (nicht leer, PNG- / WAV-Signatur); hochgeladen werden genau die geprüften Bytes.
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
* Die Prüfung erfolgt pro Eintrag. Eine Datei, die kein wohlgeformtes JSON ist, deren `SchemaVersion` fehlt oder nicht unterstützt wird oder die kein `DocumentFiles`-Array hat, wird als Ganzes abgelehnt (`InputSchemaException`). Ein fehlerhafter Eintrag fehlt in `InputDocument.Candidates` und steht mit allen Problemen in `InputDocument.Rejected`; die gültigen Einträge werden trotzdem zurückgegeben und können veröffentlicht werden. Abgelehnte Einträge werden nie veröffentlicht. `Rejected` sollte dem Nutzer angezeigt werden: Der Veröffentlichungslauf meldet sie nicht. Nach dem Korrigieren der Datei kann der Lauf wiederholt werden: Bereits veröffentlichte Einträge werden als Duplikate übersprungen.
* Fehler pro Eintrag: Belegpfade müssen absolut sein (Windows- oder Unix-Schreibweise) und auf `.png` / `.wav` enden; `Latitude` und `Longitude` beide `0` (fehlende GPS-Position) wird abgelehnt; ein `Date` in der Zukunft (verglichen mit der aktuellen deutschen Zeit) wird abgelehnt.
* Die Datei soll eine **Referenzaufnahme** pro Art, Nacht und Standort enthalten, nicht jede Detektion: Es geht darum, eine Art an einem Ort zu einer Zeit nachzuweisen, und dafür reicht meist eine gute Aufnahme. Die Duplikatprüfung bei iNaturalist (gleiches Taxon, gleicher Kalendertag, standardmäßig im Umkreis von 100 m) überspringt einen zweiten Eintrag für dieselbe Kombination.
* `SchemaVersion` ist Pflicht. Neuere Versionen als die der Bibliothek werden abgelehnt.
* Pflicht: `Date`, `Latitude`, `Longitude`, `SpeciesLatin`, `PathToPng`, `PathToWav`.
  Optional: `SpeciesLocal`, `Temperature`, `Humidity`, `Comment`. Unbekannte Eigenschaften werden ignoriert.
* `Date` (`dd.MM.yyyy HH:mm:ss`) enthält keine Zeitzone und wird immer als **deutsche Ortszeit (Europe/Berlin)** gelesen, einschließlich Sommerzeit. Es wird nie aus der Zeitzone des Hosts oder des Rechners umgerechnet. Sie dient der Prüfung auf Zukunftsdaten (Vergleich mit der aktuellen Berliner Zeit). An iNaturalist wird nur das Kalenderdatum übertragen, nicht die Uhrzeit.
* Artnamen werden normalisiert (`Eptesicus Serotinus` wird zu `Eptesicus serotinus`). Ein Name, den
  iNaturalist nicht exakt findet, wird übersprungen und nie geraten.

## Ergebnisse

`ExportOrchestrator.RunAsync` liefert pro Kandidat ein `PublishResult` mit einem `PublishStatus`:
`Created`, `WouldCreate` (Trockenlauf), `SkippedDuplicate`, `SkippedUnresolvedTaxon`,
`SkippedMissingEvidence`, `SkippedInvalidEvidence` (nicht lesbar, leer oder keine PNG- / WAV-Datei), `Failed`. Ein fehlgeschlagenes Ergebnis kann teilweise erfolgt sein
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
