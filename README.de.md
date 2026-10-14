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
  Ohne beide Belegdateien wird nichts veröffentlicht.
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

// Auch der Probelauf meldet sich an: Ohne gespeichertes Token öffnet der erste Lauf (Probelauf oder nicht) den Browser.
// Deshalb vorher ausdrücklich anmelden, etwa über eine Schaltfläche „Mit iNaturalist verbinden“.
if (!auth.HasStoredToken)
    await auth.LoginAsync();

var input = InputSchemaReader.ReadFile(@"C:\pfad\zu\export.json");
var orchestrator = new ExportOrchestrator(publisher);

// Standard ist ein Probelauf: Er geht alle Schritte durch und schreibt nichts.
// Der Bericht enthält die abgelehnten Einträge, die Warnungen und ein Ergebnis je Kandidat.
var vorschau = await orchestrator.RunAsync(input, new PublishOptions());
foreach (var r in vorschau.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Message}");

// Wirklich veröffentlichen, nachdem der Nutzer die Vorschau gesehen und bestätigt hat.
var bericht = await orchestrator.RunAsync(input, new PublishOptions { Commit = true });
foreach (var r in bericht.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Url}");
```

## Vor dem Veröffentlichen

Veröffentlichen ist öffentlich und nicht umkehrbar. Deshalb ist ein Lauf ein Probelauf, solange nicht `Commit = true`
übergeben wird. Ein Probelauf geht alle Schritte eines echten Laufs durch (Anmeldung, Artzuordnung, Belegprüfung, Regeln
von iNaturalist, Duplikatprüfung) und hält an, wo der erste Schreibzugriff käme. `WouldCreate` und `WouldResume` sagen,
was ein echter Lauf täte; jeder Überspringen-Status ist derselbe wie im echten Lauf, der Grund steht in `Message`.
`PublishResult.Description` enthält den (deutschen) Text, der zur öffentlichen Beschreibung wird.

* **`Rejected` und `Warnings` dem Nutzer anzeigen und bestätigen lassen, bevor mit `Commit = true` veröffentlicht wird.**
  Beides liegt vor, sobald die Datei gelesen ist. Der `RunReport` von `RunAsync(InputDocument, ...)` enthält beides, ein
  Lauf nur über die Kandidaten nicht. Eine Warnung hält einen Eintrag nicht vom Veröffentlichen ab.
* Ein Probelauf kann Duplikate innerhalb einer Datei nicht vorhersehen: Mehrere Einträge für dieselbe Art, Nacht und
  denselben Ort melden alle `WouldCreate`, ein echter Lauf überspringt alle außer dem ersten.
* Der Upload selbst wird im Probelauf nicht ausprobiert. Die Belege werden gegen die dokumentierten Regeln von iNaturalist
  geprüft, ob iNaturalist sie annimmt, zeigt aber erst ein echter Lauf.
* Bietet die Anwendung keinen Probelauf an, in kleinen Stapeln veröffentlichen: Der erste Stapel dient dann als Vorschau,
  und die Duplikatprüfung macht eine Wiederholung sicher.

## Zugangsdaten

Das Paket enthält **keine** OAuth-Client-ID und kein Secret und liest keine Konfigurationsdateien und keine
Umgebungsvariablen. Die Anwendung einmal registrieren und die Werte über `INaturalistOptions` übergeben; iNaturalist
verlangt, dass die Entwicklerin oder der Entwickler der Host-Anwendung (nicht deren Nutzer) zuerst als "App Owner"
zugelassen ist. Schritte: [docs/inaturalist-setup.de.md](docs/inaturalist-setup.de.md).

Die Anmeldung hält ein `INaturalistTokenStore`; der Standard verschlüsselt sie mit Windows-DPAPI und schreibt auf anderen
Plattformen keinen Klartext, außer man stimmt zu. Einzelheiten und eigene Stores: [Token-Speicherung](docs/inaturalist-setup.de.md#token-speicherung).

## Eingabeformat

Die Eingabe ist die Exportdatei von BatInspector. Ihr Schema ist versioniert: `SchemaVersion` ist Pflicht, neuere
Versionen als die der Bibliothek werden abgelehnt. Nur eine strukturell unbrauchbare Datei wirft eine Ausnahme
(`InputSchemaException`); ein fehlerhafter Eintrag fehlt in `InputDocument.Candidates` und steht in
`InputDocument.Rejected`, ein zweifelhafter wird veröffentlicht und in `InputDocument.Warnings` gemeldet. Die Datei enthält
eine Referenzaufnahme pro Art, Nacht und Ort, nicht jede Detektion.

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

Jedes Feld, Zeitzonen, Artnamen und was abgelehnt wird oder nur warnt: [docs/input-format.de.md](docs/input-format.de.md).
Wie die Gruppenwerte von BatInspector (`Nyctaloid`, `Mbart`, ...) abgelegt werden: [Art und Taxon](docs/inaturalist.de.md#art-und-taxon).

## Ergebnisse

`ExportOrchestrator.RunAsync(InputDocument, ...)` liefert einen `RunReport`: die abgelehnten Einträge (`Rejected`) und die
Warnungen (`Warnings`) des Dokuments, ein `PublishResult` je begonnenem Kandidaten (`WarningsFor(result)`, `CountByStatus`)
und `IsComplete`. `ObservationCandidate.EntryIndex` ordnet ein Ergebnis seinem Eintrag zu. Die Status und ihre Bedeutung:
[docs/inaturalist.de.md#ergebnisse](docs/inaturalist.de.md#ergebnisse).
Was welches iNaturalist-Feld erhält und was öffentlich wird: [docs/inaturalist.de.md](docs/inaturalist.de.md);
jede Anfrage an iNaturalist: [docs/inaturalist-api.de.md](docs/inaturalist-api.de.md).

**Abbrechen** wirft keine Ausnahme. Der Lauf hält an und liefert die bisherigen Ergebnisse; der unterbrochene Kandidat ist
das letzte Ergebnis mit Status `Cancelled`, noch nicht begonnene Kandidaten bekommen kein Ergebnis. `RunReport.IsComplete`
ist bei einem abgebrochenen Lauf false. Die Überladung, die die Kandidaten nimmt, liefert nur die Ergebnisse: Dort zeigt das
eigene Token, ob abgebrochen wurde.

**Erneuter Lauf nach einem Teilergebnis:** Ein `Failed`- oder `Cancelled`-Ergebnis kann teilweise erfolgt sein und braucht
kein Aufräumen. Einfach erneut laufen lassen: Die Duplikatprüfung überspringt, was vorhanden ist, und eine unvollständige
Beobachtung, die dieses Paket für denselben Eintrag angelegt hat, bekommt die fehlenden Belege (`Resumed`). Es wird nie
etwas gelöscht. Einzelheiten: [Erneuter Lauf: Duplikate und Fortsetzen](docs/inaturalist.de.md#erneuter-lauf-duplikate-und-fortsetzen).

**Rate und Wiederholungen:** Anfragen werden im Abstand von einer Sekunde gesendet (`INaturalistOptions.MinRequestInterval`).
Fehlgeschlagene Anfragen werden standardmäßig nicht wiederholt; mit `INaturalistOptions.MaxAttempts = 3` wird wiederholt,
und zwar nur, was nichts doppelt anlegen kann. Einzelheiten: [Anfragerate und Wiederholungen](docs/inaturalist-setup.de.md#anfragerate-und-wiederholungen).

## Logging

Die Bibliothek loggt über `Microsoft.Extensions.Logging.Abstractions`; jede Klasse, die loggt, nimmt einen optionalen `ILogger`, ohne ihn wird nichts geschrieben.
Debug protokolliert die HTTP-Aufrufe, Information die Ergebnisse, Warning die Ausweichfälle, Error einen `Failed`-Kandidaten mit seiner Ausnahme. Tokens werden nie protokolliert.
Stufen, Ereignis-IDs und wie man einen Konsolen- oder Datei-Logger anschließt: [docs/logging.de.md](docs/logging.de.md).

## Entwicklung

```text
dotnet build
dotnet test
dotnet format
```

VS-Code-Tasks dafür stehen in `.vscode/tasks.json`. Siehe [CONTRIBUTING.md](CONTRIBUTING.md).
Ein vollständiger Beispiel-Host (Anmeldung, Probelauf, Veröffentlichen mit Bestätigung): [tools/SmokeTest/Program.cs](tools/SmokeTest/Program.cs).
Die Dokumentation wird auf Englisch und Deutsch gepflegt (`*.md` / `*.de.md`).

## Lizenz

[MIT](LICENSE)
