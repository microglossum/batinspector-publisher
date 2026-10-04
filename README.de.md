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

// Standard ist ein Probelauf: Er geht alle Schritte durch und schreibt nichts.
// Der Bericht enthält die abgelehnten Einträge, die Warnungen und ein Ergebnis je Kandidat.
var vorschau = await orchestrator.RunAsync(input, new PublishOptions());
foreach (var r in vorschau.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Message}");

// Wirklich veröffentlichen (der erste Aufruf öffnet den Browser zur iNaturalist-Anmeldung).
var bericht = await orchestrator.RunAsync(input, new PublishOptions { Commit = true });
foreach (var r in bericht.Results)
    Console.WriteLine($"{r.Candidate.ScientificName}: {r.Status} {r.Url}");
```

## Vor dem Veröffentlichen

Veröffentlichen ist öffentlich und nicht umkehrbar. Deshalb ist ein Lauf ein Probelauf, solange nicht `Commit = true` übergeben wird. Wenn möglich, zuerst einen Probelauf machen. Er geht alle Schritte eines echten Laufs durch (Anmeldung, Artzuordnung, Belegprüfung, Regeln von iNaturalist, Duplikatprüfung) und hält an, wo der erste Schreibzugriff käme. Er meldet also, was ein echter Lauf täte:

* `WouldCreate`: Ein echter Lauf legt die Beobachtung an und hängt Spektrogramm und Audio an.
* `WouldResume`: Ein früherer Lauf hat eine unvollständige Beobachtung dieses Eintrags hinterlassen (`ObservationId`); ein echter Lauf hängt nach, was fehlt.
* `SkippedDuplicate`, `SkippedUnresolvedTaxon`, `SkippedInvalidEntry`, `SkippedInvalidEvidence`, `SkippedMissingEvidence`: dasselbe Ergebnis wie im echten Lauf, der Grund steht in `Message`.
* `PublishResult.Description` enthält den (deutschen) Text, der zur öffentlichen Beschreibung wird.

`ExportOrchestrator.RunAsync(InputDocument, ...)` liefert einen `RunReport`: die abgelehnten Einträge (`Rejected`) und die Warnungen (`Warnings`) des Dokuments neben den Ergebnissen (`WarningsFor(result)`, `CountByStatus`). Ein Objekt reicht also, um dem Nutzer vor dem echten Lauf alles zu zeigen. `IsComplete` ist bei einem abgebrochenen Lauf false. `ObservationCandidate.EntryIndex` ist die Position in der Datei.

Was ein Probelauf nicht sagen kann:

* **Mehrere Einträge für dieselbe Art, Nacht und denselben Ort melden alle `WouldCreate`.** Es wird nichts angelegt, deshalb sieht die Duplikatprüfung einen früheren Eintrag derselben Datei nicht. Ein echter Lauf legt den ersten an und überspringt die späteren als `SkippedDuplicate`. Die Datei soll ohnehin eine Referenzaufnahme je Art, Nacht und Ort enthalten (siehe „Eingabeformat“); `Warnings` meldet Einträge, die sich genau wiederholen, mehr lässt sich vorab nicht prüfen.
* Der Upload selbst wird nicht ausprobiert. Die Belege werden gegen die dokumentierten Regeln von iNaturalist geprüft (Typ, Größe), ob iNaturalist sie annimmt, zeigt aber erst ein echter Lauf.
* Das Ergebnis ist eine Momentaufnahme: Beobachtungen, die danach auf iNaturalist entstehen oder verschwinden, sind nicht vorhergesehen.

Bietet die Anwendung keinen Probelauf an, hängen die wichtigsten Prüfungen nicht davon ab:

* `InputDocument.Rejected` und `InputDocument.Warnings` liegen vor, sobald die Datei gelesen ist. Ein Lauf, der nur die Kandidaten bekommt, meldet sie nie (die Überladung mit `RunReport` tut es): **beides dem Nutzer anzeigen und bestätigen lassen, bevor mit `Commit = true` veröffentlicht wird**. Eine Warnung hält einen Eintrag nicht vom Veröffentlichen ab.
* Die Regeln von iNaturalist und die Prüfung der Belege laufen vor jeder Veröffentlichung, ob Probelauf oder nicht; ein Eintrag, der sie verletzt, wird mit dem Grund in `PublishResult.Message` übersprungen.
* In kleinen Stapeln veröffentlichen statt die ganze Datei auf einmal. Der erste Stapel dient dann als Vorschau, und die Duplikatprüfung macht eine Wiederholung sicher.

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
  Windows-DPAPI und schreibt auf anderen Plattformen keinen Klartext, außer man stimmt ausdrücklich zu
  (`allowPlaintextOnNonWindows: true`). Die Klartextdatei wird mit Modus `0600` angelegt und atomar geschrieben;
  das schützt nur vor anderen lokalen Benutzern, nicht vor anderen Prozessen desselben Benutzers. Für echte sichere
  Speicherung unter Linux oder macOS einen `INaturalistTokenStore` auf Basis des Schlüsselbunds des Betriebssystems
  implementieren. Kann ein Store nicht speichern (`CanSave` ist false), schlägt die Anmeldung fehl, bevor sich der
  Browser öffnet. Eine unlesbare Token-Datei gilt als „kein Token“.
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
* Die Prüfung erfolgt pro Eintrag. Eine Datei, die kein wohlgeformtes JSON ist, deren `SchemaVersion` fehlt oder nicht unterstützt wird oder die kein `DocumentFiles`-Array hat, wird als Ganzes abgelehnt (`InputSchemaException`). Ein fehlerhafter Eintrag fehlt in `InputDocument.Candidates` und steht mit allen Problemen in `InputDocument.Rejected`; die gültigen Einträge werden trotzdem zurückgegeben und können veröffentlicht werden. Abgelehnte Einträge werden nie veröffentlicht. `Rejected` sollte dem Nutzer angezeigt werden: Ein Lauf, der nur die Kandidaten bekommt, meldet sie nicht, der `RunReport` schon. Nach dem Korrigieren der Datei kann der Lauf wiederholt werden: Bereits veröffentlichte Einträge werden als Duplikate übersprungen.
* Fehler pro Eintrag: Belegpfade müssen absolut sein (Windows- oder Unix-Schreibweise) und auf `.png` / `.wav` enden; `Latitude` und `Longitude` beide `0` (fehlende GPS-Position) wird abgelehnt; ein `Date` in der Zukunft (verglichen mit der aktuellen deutschen Zeit) wird abgelehnt.
* Warnungen pro Eintrag (`InputDocument.Warnings`): Der Eintrag wird akzeptiert und veröffentlicht, aber etwas ist zweifelhaft; auch diese sollten angezeigt werden. Eine `Temperature` außerhalb von -40 bis 60 °C oder eine `Humidity` außerhalb von 0 bis 100 % wird nicht in die Beobachtung übernommen (Sensor-Fehlercodes wie -127 oder 85 landen hier); ein `Date` zwischen 09:00 und 15:59 Ortszeit (Fledermäuse fliegen nachts: Geräteuhr und `TimeZone` prüfen); ein `SpeciesLatin` aus drei oder mehr Wörtern (es wird keinem Taxon zugeordnet); ein Eintrag mit derselben Art, Zeit und Position wie ein früherer.
* Die Datei soll eine **Referenzaufnahme** pro Art, Nacht und Standort enthalten, nicht jede Detektion: Es geht darum, eine Art an einem Ort zu einer Zeit nachzuweisen, und dafür reicht meist eine gute Aufnahme. Die Duplikatprüfung bei iNaturalist (gleiches Taxon, gleicher Kalendertag, standardmäßig im Umkreis von 100 m) überspringt einen zweiten Eintrag für dieselbe Kombination.
* `SchemaVersion` ist Pflicht. Neuere Versionen als die der Bibliothek werden abgelehnt.
* Pflicht: `Date`, `Latitude`, `Longitude`, `SpeciesLatin`, `PathToPng`, `PathToWav`.
  Optional: `SpeciesLocal`, `Temperature`, `Humidity`, `Comment`, `TimeZone`. Unbekannte Eigenschaften werden ignoriert.
* `Date` (`dd.MM.yyyy HH:mm:ss`) enthält keinen Offset. Sie wird in der Zone des optionalen `TimeZone` gelesen (IANA-Id wie `Europe/Lisbon`; eine unbekannte Id lässt den Eintrag ablehnen) oder, ohne `TimeZone`, als **deutsche Ortszeit (Europe/Berlin)**, einschließlich Sommerzeit. Gespeichert wird sie mit dem Offset dieser Zone an diesem Datum (`ObservationCandidate.ObservedAt` ist ein `DateTimeOffset`). Es wird nie aus der Zeitzone des Hosts oder des Rechners umgerechnet. Eine Uhrzeit, die es nicht gibt (die beim Vorstellen der Uhr übersprungene Stunde), lässt den Eintrag ablehnen; eine Uhrzeit in der beim Zurückstellen doppelten Stunde wird als Normalzeit gelesen und in `InputDocument.Warnings` gemeldet (der Eintrag wird trotzdem veröffentlicht). Ein Offset im Wert (`Z`, `+02:00`) ist ein Formatfehler: die Zone stattdessen mit `TimeZone` angeben. Duplikatprüfung und das an iNaturalist gesendete Datum verwenden den lokalen Kalendertag dieser Zone. Die Prüfung auf Zukunftsdaten vergleicht Zeitpunkte. Der Host braucht die Zeitzonendaten für Europe/Berlin (ohne sie wirft der Reader eine `TimeZoneNotFoundException`). An iNaturalist wird nur das Kalenderdatum übertragen, nicht die Uhrzeit.
* Artnamen werden normalisiert (`Eptesicus Serotinus` wird zu `Eptesicus serotinus`). Ein Name aus zwei Wörtern muss exakt zu einer aktiven iNaturalist-Art passen, ein Name aus einem Wort zu einer aktiven Gattung (`Myotis`).
  Ein Name, der nichts findet, nur als Synonym vorkommt, den falschen Rang hat oder mehrdeutig ist, wird mit dem Grund in `PublishResult.Message` übersprungen und nie geraten. Auch ein Name mit drei oder mehr Wörtern (`Myotis cf. daubentonii`) wird übersprungen.
* Die Gruppen- und Unsicherheitswerte von BatInspector sind die einzige Ausnahme: `Nyctaloid`, `Social` und `?` werden unter der Ordnung Chiroptera abgelegt, `Mbart` unter der Gattung Myotis. Der ursprüngliche Wert bleibt als Artvorschlag der Beobachtung erhalten und wird in der Beschreibung genannt; `PublishResult.TaxonName` nennt das verwendete Taxon. Auf iNaturalist lässt es sich verfeinern.
  Andere Werte, die kein Taxonname sind (`todo`, ein unbekanntes BatInspector-Kürzel, ein Tippfehler), werden übersprungen, damit ein korrigierter erneuter Lauf keine zweite Beobachtung anlegt.

BatInspector ist Open Source ([chrmue44/BatInspector](https://github.com/chrmue44/BatInspector)); sein Code ist die Referenz dafür, was ein Feld oder Wert bedeutet (zum Beispiel die Artenliste in `BatInfo.cs`). Der Export und seine neuesten Werte können auf einem anderen Branch als `main` liegen oder noch nicht veröffentlicht sein, der öffentliche Code kann also hinter dem zurückliegen, was BatInspector tatsächlich schreibt.

## Ergebnisse

`ExportOrchestrator.RunAsync` liefert pro Kandidat ein `PublishResult` mit einem `PublishStatus`:
`Created`, `WouldCreate` und `WouldResume` (Probelauf), `SkippedDuplicate`, `SkippedUnresolvedTaxon`,
`SkippedMissingEvidence`, `SkippedInvalidEvidence` (nicht lesbar, leer, keine PNG- / WAV-Datei oder größer, als die Plattform akzeptiert), `SkippedInvalidEntry` (der Eintrag verletzt eine Regel dieser Plattform, etwa ein Datum oder eine Position, die sie ablehnt), `Resumed`, `Failed`, `Cancelled`.

Ein fehlgeschlagenes oder abgebrochenes Ergebnis kann teilweise erfolgt sein (Beobachtung angelegt, Belege unvollständig): dann ist `ObservationId` gesetzt, `SpectrogramAttached` / `AudioAttached` sagen, was vorhanden ist,
und `InterruptedStep` nennt den Schritt, an dem es stoppte. Kam der Abbruch, während die Beobachtung angelegt wurde, ist unbekannt, ob sie existiert; der nächste Lauf klärt das.

**Abbrechen** wirft keine Ausnahme. `RunAsync` hält an und liefert die bisherigen Ergebnisse; der unterbrochene Kandidat ist das letzte Ergebnis mit Status `Cancelled`. Noch nicht begonnene Kandidaten bekommen kein Ergebnis. Ob der Lauf abgebrochen wurde oder fertig ist, zeigt das eigene Token.

**Erneuter Lauf nach einem Teilergebnis:** Die Duplikatprüfung überspringt normalerweise eine vorhandene Beobachtung (`SkippedDuplicate`). Ist die vorhandene Beobachtung eine, die dieses Paket für denselben Eintrag angelegt hat
(identische Beschreibung) und fehlt ihr noch das Spektrogramm oder die Audioaufnahme, hängt der Lauf die fehlenden Belege an und meldet `Resumed`. Jede andere Beobachtung des Nutzers bleibt unberührt. Es wird nie etwas gelöscht.

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

VS-Code-Tasks dafür stehen in `.vscode/tasks.json`. Konventionen: [CLAUDE.md](CLAUDE.md).
Die Dokumentation wird auf Englisch und Deutsch gepflegt (`*.md` / `*.de.md`).

## Lizenz

[MIT](LICENSE)
