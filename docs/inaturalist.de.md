# iNaturalist: was wohin gelangt

English version: [inaturalist.md](inaturalist.md)

Diese Seite beschreibt, was `INaturalistPublisher` mit einem Eintrag der Eingabedatei macht: welches Feld zu welchem
iNaturalist-Feld wird, die Schritte eines Laufs, die Ergebnisse und was danach öffentlich ist. Registrierung, Anmeldung und
Ratenbegrenzung stehen in [inaturalist-setup.de.md](inaturalist-setup.de.md); wie die Eingabedatei gelesen wird, steht in
[input-format.de.md](input-format.de.md).

Alles hier ist aus dem Code sowie aus dem Open-Source-Code und dem Forum von iNaturalist abgeleitet, und der Ablauf wurde am 2026-10-15
in einem Live-Lauf gegen iNaturalist geprüft. Was ein Live-Lauf noch nicht gezeigt hat, ist als solches gekennzeichnet.

## Die Schritte eines Kandidaten

```mermaid
flowchart TD
    A["Kandidat + Belege<br/>(einmal gelesen und geprüft)"] --> B{"Regeln von iNaturalist<br/>(Datum, Position, Größe, ...)"}
    B -->|verletzt| S1["SkippedInvalidEntry /<br/>SkippedInvalidEvidence"]
    B -->|ok| C["Anmeldung (gespeichertes Token oder Browser)"]
    C --> D{"Taxon: genau ein<br/>aktiver Fledermaus-Treffer?"}
    D -->|nein| S2["SkippedUnresolvedTaxon"]
    D -->|ja| E{"Für diesen Eintrag schon<br/>veröffentlicht? (am Tag gefunden)"}
    E -->|"ja, vollständig"| S3["SkippedDuplicate"]
    E -->|"ja, Foto oder Ton fehlt"| G{"Commit?"}
    E -->|nein| H{"Commit?"}
    G -->|nein| W1["WouldResume"]
    G -->|ja| K["Fehlendes anhängen"]
    H -->|nein| W2["WouldCreate"]
    H -->|ja| I["Beobachtung anlegen"]
    I --> J["Spektrogramm anhängen (Foto)"]
    J --> K2["Audio anhängen (Ton)"]
    K --> R["Resumed"]
    K2 --> CR["Created"]
```

Vor dem ersten Schritt hat der Orchestrator beide Belegdateien gelesen und geprüft (siehe
[input-format.de.md](input-format.de.md#belegdateien)); eine fehlende Datei ist `SkippedMissingEvidence`. Ein **Probelauf**
(Standard, `PublishOptions.Commit = false`) durchläuft jeden Schritt einschließlich Anmeldung, Taxonsuche und
Duplikatprüfung und hält an, wo der erste Schreibzugriff käme. Im Probelauf wird nichts angelegt.

Die Schritte verwenden diese Endpunkte von iNaturalist (jede Anfrage mit ihren Parametern steht in [inaturalist-api.de.md](inaturalist-api.de.md)):

| Schritt | Anfrage |
|---|---|
| Taxon | `GET /v2/taxa/autocomplete` |
| Duplikatprüfung, Fortsetzen | `GET /v1/observations` (nach dem Tag des Eintrags, eingeschränkt mit `user_id`, die aus `GET /v1/users/me` stammt) |
| Anlegen | `POST /v2/observations` |
| Spektrogramm | `POST /v2/observation_photos` (eine Multipart-Anfrage, die hochlädt und verknüpft) |
| Audio | `POST /v2/observation_sounds` (ebenso) |

## Vom Eingabefeld zum iNaturalist-Feld

| Eingabe | iNaturalist | Hinweise |
|---|---|---|
| `SpeciesLatin` | `taxon_id` | Live aufgelöst, siehe [Art und Taxon](#art-und-taxon). |
| `SpeciesLatin` | `species_guess` | Der Name aus der Datei, normalisiert. Bei einem unsicheren Ruf wie `Nyctaloid` bleibt hier der ursprüngliche Wert, während `taxon_id` das übergeordnete Taxon ist. |
| `Date` | `observed_on_string` | Lokales Datum und Uhrzeit als `yyyy-MM-dd HH:mm:ss`, ohne Zone oder Offset (siehe [Zeitzone](#zeitzone)). Die Zeit mit ihrer Zone steht auch in der Beschreibung. |
| `Latitude`, `Longitude` | `latitude`, `longitude` | Unverändert. Eine Positionsgenauigkeit wird nicht gesendet. |
| `Latitude`, `Longitude` | `place_guess` | Nicht gesendet. Die Eingabe hat keinen Ortsnamen; iNaturalist setzt aus den Koordinaten selbst einen, siehe [Position verbergen](inaturalist-setup.de.md#position-verbergen). |
| `Temperature`, `Humidity`, `Comment`, `Date`, `TimeZone` | `description` | Deutscher Text, siehe unten. |
| `PathToPng` | Foto | Das Spektrogramm, hochgeladen unter seinem Dateinamen. |
| `PathToWav` | Ton | Die Aufnahme, hochgeladen unter ihrem Dateinamen. |
| `SpeciesLocal` | nicht gesendet | |
| (Optionen) | `tag_list` | `INaturalistOptions.TagList`, Standard `bat,acoustic-monitoring,batinspector`, dazu das Fortsetzungs-Tag des Eintrags (siehe unten). |
| (Optionen) | `geoprivacy` | `INaturalistOptions.Geoprivacy`, standardmäßig keine. |
| (Optionen) | `license`, Foto-`license_code` | `INaturalistOptions.License`, standardmäßig keine (dann gelten die Standardlizenzen des Kontos). Nicht für den Sound, siehe [Lizenzen](inaturalist-setup.de.md#lizenzen). |
| `SpeciesLatin`, `Date`, `Latitude`, `Longitude` | `tag_list` | Ein weiteres Tag, `batinspector:` und 12 Hex-Ziffern: ein kurzer Hash aus der Art wie in der Datei, dem Zeitpunkt in UTC und den Koordinaten. Es kennzeichnet die Beobachtung als die, die dieses Paket für den Eintrag angelegt hat, siehe [Erneuter Lauf](#erneuter-lauf-duplikate-und-fortsetzen). |

Nicht gesendet werden: eine Zeitzone, eine Positionsgenauigkeit, Captive/Cultivated-Kennzeichen. Das Paket behandelt
sensible Arten nicht; iNaturalist verschleiert die Koordinaten von Taxa, die es für sensibel hält, selbst. Die Position aller
Beobachtungen lässt sich mit `INaturalistOptions.Geoprivacy` verbergen, siehe [Position verbergen](inaturalist-setup.de.md#position-verbergen).

### Zeitzone

Jede Beobachtung wird mit Koordinaten gesendet, und iNaturalist übernimmt die Zeitzone dann **aus den Koordinaten**: Es liest
`observed_on_string` als Ortszeit an diesem Ort und ignoriert eine Zone oder einen Offset im Text sowie einen
`time_zone`-Parameter (laut Quellcode; live gesehen: Die gesendete Ortszeit kommt mit der Zone des Ortes zurück, auch nach Mitternacht und für Lisbon). Das Paket sendet deshalb die Uhrzeit des Eintrags, wie sie
ist, und keine Zone. Das stimmt, solange `TimeZone` (Standard Europe/Berlin) die Zone des Ortes ist, an dem aufgenommen
wurde; so ist das Eingabefeld gemeint. Ist sie es nicht (etwa ein Rekorder, der in Portugal auf deutscher Zeit blieb),
zeigt iNaturalist die Zeit um die Differenz verschoben; die Beschreibung nennt weiterhin die Zone, in der der Eintrag gelesen wurde.

### Die Beschreibung

Die Beschreibung ist mit Absicht deutsch (alle Nutzer des Pakets sind deutschsprachig, und sie ist öffentlicher Inhalt auf der
Plattform). Sie besteht aus diesen Teilen, jeweils nur, wenn die Daten da sind:

1. `INaturalistOptions.DescriptionPrefix` (Standard: *Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung).*)
2. Die Aufnahmezeit mit Zone: `Exemplarische Ruferkennung vom 13.06.2026 04:26:34 Uhr MESZ.` Europe/Berlin heißt `MEZ` oder
   `MESZ`; jede andere Zone wird ausdrücklich genannt (`(Zeitzone Europe/Lisbon, UTC+01:00)`), nie aus dem Offset erraten.
3. Bei einem unsicheren Ruf, der unter einem übergeordneten Taxon abgelegt wird: `Bestimmung in BatInspector: "Nyctaloid" (keine sichere Artbestimmung), hier als Chiroptera eingetragen.`
4. `Temperatur: 20,5 °C.` und `Luftfeuchte: 92,4 %.` (höchstens eine Nachkommastelle, deutsches Dezimalkomma)
5. `Anmerkung zur Bestimmung: <Comment>`
6. `Beleg: Spektrogramm und Audioaufnahme dieser Ruferkennung.`

Für den Eintrag im [Beispiel](input-format.de.md#beispiel) lautet die Beschreibung:

> Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung). Exemplarische Ruferkennung vom 13.06.2026 04:26:34 Uhr MESZ. Temperatur: 20,5 °C. Luftfeuchte: 92,4 %. Beleg: Spektrogramm und Audioaufnahme dieser Ruferkennung.

`PublishResult.Description` enthält den Text, auch im Probelauf, damit der Host zeigen kann, was öffentlich würde. Die Formulierung
kann sich zwischen Versionen ändern; ein späterer Lauf erkennt seine Beobachtungen an ihrem Tag, nicht am Text.

## Art und Taxon

Taxa werden **für jeden Kandidaten live** mit `taxa/autocomplete` aufgelöst und nie zwischengespeichert oder als Liste
mitgeliefert. Ein Name wird nur akzeptiert, wenn genau ein aktives Taxon genau diesen Namen und den erwarteten Rang hat
(zwei Wörter: Art; ein Wort: Gattung), und wenn dieses Taxon eine Fledermaus ist: Chiroptera selbst oder ein Taxon darunter.
Auch Chiroptera wird live über den Namen aufgelöst. Es wird nichts geraten: kein „erster Treffer der Autovervollständigung“,
kein übergeordnetes Taxon für einen Tippfehler.

| Situation | Ergebnis |
|---|---|
| Ein aktives Taxon, Name und Rang passen | Wird verwendet. |
| Mehrere Treffer | `SkippedUnresolvedTaxon`, mehrdeutig (die IDs stehen in der Meldung). Ein Treffer außerhalb der Fledermäuse zählt nicht, ein gleichnamiges Taxon in einer anderen Gruppe macht einen Fledermausnamen also nicht mehrdeutig. |
| Der Name existiert, aber nicht unter Chiroptera (oder iNaturalist meldet seine Abstammung nicht) | `SkippedUnresolvedTaxon`, keine Fledermaus. |
| Der Name existiert, aber nur mit anderem Rang | `SkippedUnresolvedTaxon`, falscher Rang. |
| Der Name ist ein Synonym | `SkippedUnresolvedTaxon`, mit dem aktuellen Namen. Die Eingabedatei korrigieren. |
| Nicht gefunden | `SkippedUnresolvedTaxon`, nicht gefunden. |
| Drei oder mehr Wörter | `SkippedUnresolvedTaxon` ohne Anfrage. |

Die Gruppen- und Unsicherheitswerte von BatInspector sind die einzige Ausnahme, eine kleine feste Tabelle, die immer gilt und
keine Option hat:

| `SpeciesLatin` | Abgelegt unter | `species_guess` |
|---|---|---|
| `Nyctaloid`, `Social`, `?` | Chiroptera (Ordnung) | der ursprüngliche Wert |
| `Mbart` | *Myotis* (Gattung) | `Mbart` |

`PublishResult.TaxonName` nennt das Taxon, unter dem eine Beobachtung abgelegt ist (oder im Probelauf abgelegt würde). Der Nutzer kann
die Bestimmung auf iNaturalist verfeinern. `todo` und jeder andere Wert, der kein Taxonname ist, wird übersprungen.

## Erneuter Lauf: Duplikate und Fortsetzen

Bevor etwas angelegt wird, fragt der Publisher iNaturalist, ob dieses Paket den Eintrag schon veröffentlicht hat. Gesucht wird das
`batinspector:`-Tag des Eintrags unter den **eigenen** Beobachtungen des angemeldeten Nutzers (`user_id`, seine numerische Nutzer-ID).
Das Tag wird gefunden, wo auch immer die Beobachtung liegt, auch wenn ihre Position verschleiert oder privat ist (durch `Geoprivacy` oder
bei einem sensiblen Taxon durch iNaturalist selbst).

- **Nichts gefunden:** Die Beobachtung wird angelegt.
- **Gefunden, und sie ist vollständig:** `SkippedDuplicate`. Die vorhandene Beobachtung steht in `ObservationId`. Sie wird nie angefasst.
- **Gefunden, und es fehlt Foto oder Ton:** Ein früherer Lauf hat sie angelegt und ist vor dem vollständigen Beleg abgebrochen. Der
  fehlende Beleg wird angehängt (`Resumed`, im Probelauf `WouldResume`). Vervollständigt wird nur eine Beobachtung, die das Tag trägt.

Sagt das Suchergebnis nicht, ob Fotos und Töne vorhanden sind, bleibt die Beobachtung mit Absicht unberührt
(`SkippedDuplicate`): Eine falsche Annahme bleibt so folgenlos.

Was man über diese Prüfung wissen sollte:

- Das Tag ist ein Hash aus der Art wie in der Datei, dem Zeitpunkt und den Koordinaten. Es erkennt denselben Eintrag wieder und sonst
  nichts: Eine Beobachtung, die anders entstanden ist (von Hand, mit einem älteren Werkzeug oder mit entferntem Tag), wird nicht erkannt und
  bleibt unberührt, und ein Eintrag, dessen Zeit, Position oder Name nach dem Veröffentlichen geändert wurde, zählt als neuer Eintrag. Die
  Auswahl einer Referenzaufnahme pro Art, Nacht und Ort ist Sache des Exports (siehe
  [input-format.de.md](input-format.de.md#eine-referenzaufnahme-pro-art-nacht-und-ort)); der Probelauf kann mehrere Einträge einer Datei, die ein
  Commit-Lauf veröffentlichen würde, nicht vorhersehen, weil er nichts anlegt, und meldet für alle `WouldCreate`.
- Das Tag ist an der Beobachtung sichtbar. Wer es entfernt, schaltet die Prüfung für diese Beobachtung ab.
- Beobachtungen, die nach der Prüfung bei iNaturalist auftauchen oder verschwinden, werden nicht vorhergesehen. Eine neue
  Beobachtung braucht einen Moment, bis sie in der Suche von iNaturalist erscheint.

## Ergebnisse

`PublishResult.Status` nimmt einen dieser Werte an (der Grund steht immer in `Message`):

| Status | Bedeutung |
|---|---|
| `Created` | Beobachtung angelegt, Spektrogramm und Audio angehängt. `ObservationId` ist gesetzt, `Url` nur, wenn iNaturalist eine numerische ID geliefert hat (siehe unten). |
| `WouldCreate` | Probelauf: Ein echter Lauf würde sie anlegen. Es wurde nichts geschrieben. |
| `WouldResume` | Probelauf: Ein echter Lauf würde einer unvollständigen Beobachtung eines früheren Laufs (in `ObservationId` genannt) das Fehlende anhängen. Es wurde nichts geschrieben. |
| `Resumed` | Eine unvollständige Beobachtung eines früheren Laufs wurde vervollständigt (in `ObservationId` genannt). Keine neue Beobachtung. |
| `SkippedDuplicate` | Eine gleichwertige Beobachtung des Nutzers existiert bereits. Sie steht in `ObservationId`, wenn sie bekannt ist, und bleibt unberührt. |
| `SkippedUnresolvedTaxon` | Der Name wurde nicht zu genau einem Taxon aufgelöst, siehe [Art und Taxon](#art-und-taxon). |
| `SkippedInvalidEntry` | Der Eintrag verletzt eine Regel von iNaturalist ([unten](#vor-dem-schreiben-geprüfte-regeln)) oder eine plattformneutrale Regel. Es wurde nichts geschrieben. |
| `SkippedInvalidEvidence` | Eine Datei ist nicht lesbar, leer, kein PNG / WAV oder größer als `MaxEvidenceBytes`. Es wurde nichts geschrieben. |
| `SkippedMissingEvidence` | Eine Datei existiert nicht. Es wurde nichts geschrieben. |
| `Failed` | Ein Fehler; die Ausnahme steht in `Error`. Kann teilweise sein: `ObservationId` ist gesetzt, wenn die Beobachtung existiert, `SpectrogramAttached` und `AudioAttached` sagen, was vorhanden ist, `InterruptedStep` nennt die Stufe. |
| `Cancelled` | Der Aufrufer hat abgebrochen. Dieselben Teilinformationen wie bei `Failed`, ohne `Error`. |

`ObservationId` ist die numerische Beobachtungs-ID von iNaturalist, `Url` ihre öffentliche Seite. Liefert iNaturalist zu einer
Beobachtung nur die UUID und keine numerische ID, ist `ObservationId` die UUID und `Url` die Adresse der Seite mit der UUID, die
iNaturalist auf die Beobachtung umleitet (das ist die übliche Antwort auf eine angelegte Beobachtung; ein späterer Lauf findet die
numerische ID). Das gilt für jeden Status, der eine Beobachtung nennt.

`Message` ist ein kurzer englischer Text des Pakets, gedacht für ein Log oder eine Detailansicht; eine Anwendung, die die
Ergebnisse in einer anderen Sprache zeigt, übersetzt den Status, nicht den Text. Eine Fehlermeldung von iNaturalist selbst wird
unverändert durchgereicht, weder übersetzt noch umformuliert: Ihr Antworttext ist Teil der Ausnahme in `Error` und von `Message`,
auf die ersten 1000 Zeichen gekürzt (`INaturalistApiException.ResponseBody` behält alles, siehe [logging.de.md](logging.de.md)).

Ein `Failed`- oder `Cancelled`-Ergebnis braucht kein Aufräumen: Bei einem erneuten Lauf vervollständigen Duplikatprüfung und
Fortsetzen es. Kam der Abbruch, während die Beobachtung angelegt wurde, ist unbekannt, ob sie existiert; der nächste Lauf
findet es heraus.

### Vor dem Schreiben geprüfte Regeln

Die eigene Prüfung des Adapters auf die Grenzen von iNaturalist (Dateigröße, Datum, Position, `species_guess`, Tags) läuft vor
der Anmeldung, auch im Probelauf, damit für einen Eintrag, den iNaturalist ablehnen würde, nichts geschrieben wird. Ein Verstoß
ergibt `SkippedInvalidEntry` (bei den Belegdateien `SkippedInvalidEvidence`), die Probleme stehen in `Message`. Was iNaturalist
danach trotzdem ablehnt, endet als `Failed`, mit dem unverändert durchgereichten Fehlertext von iNaturalist. Die Regeln, ihre
Quellen und die offenen Fragen stehen in
[inaturalist-setup.de.md](inaturalist-setup.de.md#grenzen-und-regeln-die-vor-dem-veröffentlichen-geprüft-werden).

## Was öffentlich und nicht rückgängig zu machen ist

Ein echter Lauf veröffentlicht auf dem Konto des angemeldeten Nutzers:

- die Beobachtung mit ihren **genauen Koordinaten** (sofern iNaturalist ein sensibles Taxon nicht verschleiert oder `Geoprivacy` gesetzt ist), Datum, Taxon,
  Tags und der Beschreibung, die die Messwerte und die Anmerkung enthält;
- das Spektrogramm als Foto und die Aufnahme als Ton, unter ihren Dateinamen.

Das Paket löscht oder ändert nie eine Beobachtung. Es löscht auch keine, deren Beleg nicht angehängt werden konnte (der
Fehler kann vorübergehend sein, und das Fortsetzen vervollständigt sie). Änderungen und Löschungen erfolgen bei iNaturalist.

Deshalb ist ein Lauf ein Probelauf, solange nicht `Commit = true` übergeben wird. Vor einem echten Lauf sollten
`RunReport.Rejected`, `RunReport.Warnings` und die Ergebnisse des Probelaufs (einschließlich `Description`) dem Nutzer angezeigt werden.
