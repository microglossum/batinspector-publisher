# iNaturalist-Einrichtung

English version: [inaturalist-setup.md](inaturalist-setup.md)

BatInspectorPublisher liefert keine iNaturalist-Zugangsdaten mit. Die Anwendung, die das Paket
nutzt, registriert einmal eine eigene OAuth-Anwendung und übergibt die Werte über `INaturalistOptions`. **Das Registrieren einer Anwendung ist bei iNaturalist beschränkt, siehe den ersten Abschnitt.**

## Voraussetzung: Zulassung als "App Owner"

**Eine iNaturalist-Anwendung anzulegen geht nicht einfach so.** iNaturalist lässt nicht jeden eine Anwendung registrieren.
Zuerst muss man als *App Owner* zugelassen werden, und das Formular in Schritt 1 funktioniert erst danach.

1. Antrag auf der App-Owner-Seite stellen: <https://www.inaturalist.org/oauth/app_owner_application>.
2. Die Seite nennt zwei Bedingungen: Das Konto muss **mindestens 2 Monate alt** sein, und man muss im letzten Monat **mindestens 10
   "improving identifications"** (verbessernde Bestimmungen) gemacht haben.
3. "Improving" ist ein feststehender Begriff, definiert im
   [Hilfeartikel von iNaturalist](https://help.inaturalist.org/en/support/solutions/articles/151000170241):
   * Es muss eine Bestimmung bei einer **fremden** Beobachtung sein. Eigene Beobachtungen zählen nicht.
   * Sie muss die Beobachtung voranbringen, zum Beispiel eine erste Bestimmung bei einer *unbestimmten* Beobachtung, die später bestätigt wird,
     oder die Verfeinerung von Gattung auf Art. Ein bloßes "Stimme zu" ist *bestätigend*, nicht verbessernd.
   * Den eigenen Stand zeigt `https://www.inaturalist.org/identifications?user_id=<Login>&category=improving&for=others`.
4. Nach dem Absenden bearbeiten Mitarbeitende von iNaturalist den Antrag manuell. Es gibt keine automatische Bestätigung und keine genannte
   Bearbeitungszeit; im Forum berichten Leute von Tagen bis Wochen. Die Wartezeit einplanen.

Was das praktisch heißt:

* **Eine Anwendung pro Host-Anwendung, nicht pro Nutzer.** Die Anwendung, die dieses Paket verteilt (BatInspector), registriert eine einzige
  iNaturalist-Anwendung und liefert deren Client ID mit. Ihre Nutzer brauchen **keine** eigene Anwendung, sie melden sich nur mit ihrem eigenen
  iNaturalist-Konto an (Schritt 3). Nur die Entwicklerin oder der Entwickler muss App Owner sein.
* Wer (noch) nicht qualifiziert ist, kann mit einer eigenen Anwendung nicht veröffentlichen. Öffentliche iNaturalist-Daten zu lesen braucht keine
  Anwendung, aber dieses Paket schreibt Beobachtungen, und das erfordert eine autorisierte (OAuth-)Anwendung.

Quellen: iNaturalist-Community-Forum, [API Application Request (Mai bis Sept. 2026)](https://forum.inaturalist.org/t/api-application-request/78965)
und [Can't register as App owner (Aug. 2025)](https://forum.inaturalist.org/t/cant-register-as-app-owner-api-authentication/68639),
dort werden die Antragsseite und Mitarbeitende von iNaturalist zitiert. Die Bedingungen kann iNaturalist ändern; den aktuellen Text auf der Antragsseite prüfen.

## 1. OAuth-Anwendung registrieren

1. Bei <https://www.inaturalist.org/> mit dem Konto anmelden, dem die Anwendung gehören soll.
2. <https://www.inaturalist.org/oauth/applications/new> öffnen.
3. Ausfüllen:

   | Feld | Wert |
   |---|---|
   | Name | Ein sprechender Name, er wird Nutzern auf dem Autorisierungsbildschirm angezeigt. |
   | Redirect URI | `http://127.0.0.1:45679/callback`. Muss exakt übereinstimmen, auch der Port. `127.0.0.1` verwenden, nicht `localhost`. |
   | Confidential | Siehe [Vertraulicher oder öffentlicher Client](#vertraulicher-oder-öffentlicher-client). |

4. **Client ID** kopieren (und das **Client Secret**, falls eines ausgestellt wurde).

### Vertraulicher oder öffentlicher Client

* **Vertraulich** (Standard): Es gibt Client ID und Client Secret. Beide in `INaturalistOptions` übergeben.
* **Öffentlich**: Eine native Desktop-Anwendung kann kein Secret sicher aufbewahren, daher ist das die technisch richtige Wahl.
  Es gibt nur eine Client ID; `ClientSecret` bleibt leer. Der Code sendet das Secret nur, wenn eines gesetzt ist.

> Ein Secret in einer weitergegebenen Anwendung ist kein Geheimnis. Wird die Anwendung an andere verteilt,
> besser einen öffentlichen Client verwenden (PKCE schützt den Ablauf).

## 2. Zugangsdaten an das Paket übergeben

```csharp
var options = new INaturalistOptions
{
    ClientId = settings.INaturalistClientId,
    ClientSecret = settings.INaturalistClientSecret, // null oder leer bei öffentlichem Client
};
```

Das Paket liest keine Dateien und keine Umgebungsvariablen. Wo die Anwendung die Werte ablegt, entscheidet sie selbst.
Die Werte nie in ein Repository einchecken.

## 3. Anmelden

```csharp
var auth = new INaturalistAuthenticator(options, httpClient);
await auth.LoginAsync(); // z. B. über einen Button "Mit iNaturalist verbinden"
```

Standardmäßig öffnet sich der Systembrowser. Um die URL in der eigenen Oberfläche anzuzeigen, einen
`AuthorizationPrompt` übergeben. Die Anmeldung endet, wenn iNaturalist auf die Redirect URI weiterleitet;
ein temporärer lokaler Listener fängt diese Anfrage ab, andere Anfragen an ihn werden ignoriert. `AuthorizationTimeout` (Standard 5 Minuten) begrenzt die Wartezeit.

Die Anmeldung hält zwei Tokens (siehe [Token-Speicherung](#token-speicherung)): das OAuth-Access-Token von iNaturalist, das nicht
abläuft, und das API-Token, ein JWT, das jeder API-Aufruf mitschickt und das etwa 24 Stunden gilt. `EnsureAuthenticatedAsync` (vom
Publisher für jeden Kandidaten aufgerufen) liefert das gespeicherte API-Token, solange es gültig ist. Ist das API-Token abgelaufen
(oder läuft es in weniger als 5 Minuten ab), tauscht es das gespeicherte OAuth-Token still und ohne Browser gegen ein neues. Die
Browser-Anmeldung ist also **einmal** nötig, bis die Person den Zugriff der Anwendung bei iNaturalist widerruft oder die gespeicherte
Anmeldung entfernt wird. Eine neue Browser-Anmeldung gibt es nur, wenn keine Anmeldung gespeichert ist oder iNaturalist das
gespeicherte OAuth-Token ablehnt (401 oder 403). Ein Netzwerkfehler bei der Erneuerung startet nie eine Anmeldung; der Kandidat endet als `Failed`.

### Port 45679

Die Anmeldung lauscht auf dem Port der registrierten Redirect URI. 45679 kollidiert nicht mit üblichen
Entwicklungsservern. Ist er auf einem Rechner belegt, versucht die Anmeldung bis zu drei vom Betriebssystem
vergebene freie Ports (als Warnung protokolliert) und sendet die passende Redirect URI an iNaturalist. Bei
einer Loopback-URI (`127.0.0.1`) ignoriert iNaturalist den Port beim Abgleich, die eine registrierte URI
genügt also. Zeigt der Browser trotzdem den Redirect-URI-Fehler von iNaturalist, den Port freigeben oder einen
anderen Port wählen, `INaturalistOptions.RedirectUri` setzen und dieselbe URI bei iNaturalist registrieren.
Lässt sich gar kein Port öffnen, schlägt die Anmeldung mit einer `INaturalistLoginException` fehl (`Reason` ist `ListenerUnavailable`).

Solange die Anmeldung wartet, ignoriert der Listener jede Anfrage, die nicht zu dieser Anmeldung gehört
(Portscan, Vorabruf des Browsers, ein anderes lokales Programm), und beantwortet sie mit 404; nur die
Umleitung mit dem `state` dieser Anmeldung beendet das Warten.

Der Host muss an beiden Stellen `127.0.0.1` sein: `HttpListener` vergleicht den `Host`-Header wörtlich, ein
Listener auf `127.0.0.1` antwortet bei `localhost` mit 404.

### Wo die Loopback-Anmeldung nicht funktioniert

Die Anmeldung braucht auf **demselben Rechner** zweierlei: Die Anwendung kann einen lokalen Port öffnen, und
der Browser, der die Anmeldung abschließt, erreicht ihn. In diesen Umgebungen scheitert eines von beiden
(bisher nicht getestet; die Ursache ergibt sich aus der Funktionsweise der Anmeldung):

| Umgebung | Was passiert | Was tun |
|---|---|---|
| Browser auf einem anderen Rechner als die Anwendung (Remotedesktop, VDI, SSH-Sitzung) | iNaturalist leitet den Browser auf `127.0.0.1` *seines* Rechners um, dort lauscht nichts. Die Anmeldung endet mit `TimedOut`. | Den Port zum Rechner des Browsers weiterleiten (bei SSH: `ssh -L 45679:127.0.0.1:45679 <Host>`) und dafür sorgen, dass der feste Port frei ist, denn ein Ersatzport lässt sich nicht vorab weiterleiten. Oder auf dem Rechner mit dem Browser anmelden. |
| Abgesicherter Rechner (Firewall oder Sicherheitssoftware blockiert lokale Listener) | Kein Port lässt sich öffnen: `ListenerUnavailable`. Oder der Browser erreicht ihn nicht: `TimedOut`. | Der Anwendung das Lauschen auf der Loopback-Adresse erlauben. Geht das nicht, ist die Loopback-Anmeldung dort nicht nutzbar. |
| Mehrere Benutzer auf einem Rechner (Terminalserver) | Der feste Port ist von der Anmeldung einer anderen Sitzung belegt; die Ersatzports springen ein. | Nichts, außer bei `ListenerUnavailable`: dann erneut versuchen, wenn die andere Anmeldung beendet ist. |
| Abgeschottete oder paketierte Anwendungen mit Loopback-Isolierung (zum Beispiel MSIX) | Die Anwendung erreicht ihren eigenen Loopback-Listener vom Browser aus eventuell nicht. | Nicht unterstützt: Die Anmeldung braucht einen Loopback-Listener. |

Eine fehlgeschlagene Anmeldung speichert nichts. Abbrechen und neu versuchen; die Anfragen anderer Programme beenden das Warten nicht.

### Warum es einen zusätzlichen JWT-Austausch gibt

Die REST-API von iNaturalist akzeptiert das rohe OAuth-Access-Token nicht als Bearer-Token (401). Das Token wird
zuerst per `GET https://www.inaturalist.org/users/api_token` (Host `www`) gegen ein JWT getauscht. Dieses JWT,
etwa 24 Stunden gültig, dient für alle API-Aufrufe und wird lokal gespeichert. Das steht nicht in der offiziellen API-Referenz.

### Wenn die Anmeldung während eines Laufs scheitert

Der Publisher holt sich für jeden Kandidaten ein Token beim Authenticator, nach den eigenen Prüfungen des Adapters und vor der ersten
Anfrage, auch im Probelauf. Ist keine brauchbare Anmeldung gespeichert, startet dieser Aufruf die Browser-Anmeldung innerhalb von
`RunAsync`. Kommt die Anmeldung nicht zustande (die Person klickt auf "Deny": `Denied`; niemand schließt sie innerhalb von
`AuthorizationTimeout` ab: `TimedOut`; kein Port lässt sich öffnen: `ListenerUnavailable`), endet dieser Kandidat als `Failed`, mit der
`INaturalistLoginException` in `PublishResult.Error` und ihrem Text in `Message`. Der Lauf geht weiter: Der nächste Kandidat startet
eine neue Browser-Anmeldung und wartet wieder, jeweils bis zu `AuthorizationTimeout` (Standard 5 Minuten). Bei einer Datei mit 50
Einträgen und einer Person, die den Zugriff ablehnt, kann sich der Browser also 50-mal öffnen. Ein Token-Speicher, der nicht speichern
kann (siehe [Token-Speicherung](#token-speicherung)), lässt jeden Kandidaten ebenso mit einer `PlatformNotSupportedException`
scheitern, ohne den Browser zu öffnen.

Was eine Host-Anwendung tun sollte:

1. Vor dem Lauf anmelden. `LoginAsync()` über einen Button "Mit iNaturalist verbinden" aufrufen, oder vor `RunAsync` `HasStoredToken`
   prüfen und bei false `LoginAsync()` aufrufen. Ein gescheitertes `LoginAsync()` wirft die Ausnahme an die Host-Anwendung, die den
   Grund anzeigen kann.
2. Den Lauf abbrechen, wenn das `Error` eines Ergebnisses eine `INaturalistLoginException` ist: im `progress`-Callback den an `RunAsync`
   übergebenen `CancellationToken` abbrechen. Der Lauf hält dann vor dem nächsten Kandidaten an, nicht gestartete Kandidaten bekommen
   kein Ergebnis, und `RunReport.IsComplete` ist false. `Progress<T>` ruft seinen Handler asynchron auf (im erfassten
   Synchronisationskontext oder im Threadpool), sodass noch ein Kandidat starten kann; ein eigenes `IProgress<PublishResult>`, das
   direkt in `Report` abbricht, hält sofort an.

Schritt 2 gilt auch mit gespeicherter Anmeldung: Hat die Person den Zugriff bei iNaturalist widerrufen, wird die Erneuerung
abgelehnt, und der Lauf fällt auf die Browser-Anmeldung zurück.

## Token-Speicherung

Nach der Anmeldung legt der Authenticator ein `INaturalistToken` in seinem Token-Speicher ab:

* `AccessToken`: das API-Token, das JWT, das jeder API-Aufruf mitschickt, etwa 24 Stunden gültig.
* `OAuthAccessToken`: das OAuth-Access-Token von iNaturalist. Es läuft nicht ab und erneuert das API-Token ohne Browser.
* `Username` (der iNaturalist-Login) und der Zeitpunkt, zu dem das API-Token geholt wurde.

iNaturalist stellt kein Refresh-Token aus, `RefreshToken` bleibt also leer. Das gespeicherte Token ist wie ein Passwort zu behandeln:
Wer das OAuth-Token hat, kann im Namen der Person veröffentlichen, bis sie den Zugriff der Anwendung bei iNaturalist widerruft.

### Der Standardspeicher

Ohne `tokenStore`-Argument verwendet der Authenticator `ProtectedFileTokenStore`. Er legt eine Datei `oauthtoken.dat` im Verzeichnis
`BatInspectorPublisher/inaturalist` unter `Environment.SpecialFolder.LocalApplicationData` von .NET ab:

| Plattform | Standardverzeichnis |
|---|---|
| Windows | `%LOCALAPPDATA%\BatInspectorPublisher\inaturalist` |
| Linux | `$XDG_DATA_HOME/BatInspectorPublisher/inaturalist`, meist `~/.local/share/BatInspectorPublisher/inaturalist` |
| macOS | `~/Library/Application Support/BatInspectorPublisher/inaturalist` |

`new ProtectedFileTokenStore(directory)` verwendet ein anderes Verzeichnis, zum Beispiel eines pro Profil der Host-Anwendung.

* **Windows:** Die Datei ist mit DPAPI für den aktuellen Windows-Benutzer verschlüsselt. Andere Benutzer können sie nicht lesen.
* **Linux und macOS:** Dort gibt es kein DPAPI, daher wirft das Speichern eine `PlatformNotSupportedException`, außer die
  Host-Anwendung entscheidet sich ausdrücklich dafür mit `new ProtectedFileTokenStore(allowPlaintextOnNonWindows: true)`. Die Datei ist
  dann unverschlüsseltes JSON, angelegt mit Dateimodus `0600` (nur der Besitzer darf lesen und schreiben). Das schützt vor anderen
  lokalen Benutzern, nicht vor anderen Programmen desselben Benutzers.
* **`CanSave`** ist unter Linux und macOS ohne diese Freigabe false. Der Authenticator prüft es, bevor sich der Browser öffnet, und
  wirft eine `PlatformNotSupportedException`, damit niemand eine Anmeldung autorisiert, die sich nicht speichern lässt.
* Die Datei wird zuerst in eine temporäre Datei geschrieben und dann über die alte verschoben; ein unterbrochener Schreibvorgang
  hinterlässt also nie ein halbes Token.
* **Eine unlesbare Datei** (beschädigt oder von einem anderen Windows-Benutzer verschlüsselt) gilt als "kein Token": Eine Warnung wird
  protokolliert (Ereignis 2201, siehe [logging.de.md](logging.de.md)), eine neue Anmeldung ist nötig, und sie ersetzt die Datei.

### Ein eigener Speicher

Um das Token woanders abzulegen (Schlüsselbund des Betriebssystems, Tresor der Host-Anwendung), `INaturalistTokenStore` implementieren
und als `tokenStore` an den Konstruktor von `INaturalistAuthenticator` übergeben:

* `Load()` liefert das gespeicherte Token oder null. Für ein Token, das sich nicht lesen lässt, null liefern statt einer Ausnahme. Der
  Authenticator liest den Speicher jedes Mal, wenn er ein Token braucht, also einmal pro Kandidat; das sollte schnell gehen.
* `Save(token)` ersetzt das bisherige Token. Das ganze `INaturalistToken` mit allen Eigenschaften aufbewahren.
* `Clear()` entfernt das Token.
* `CanSave` (Standard true): false liefern, wenn `Save` sicher scheitern würde, damit die Anmeldung scheitert, bevor sich der Browser öffnet.

### Verbinden und Trennen in der Host-Anwendung

Eine Oberfläche "Mit iNaturalist verbinden / trennen" braucht diese Member von `INaturalistAuthenticator`:

| Member | Was es tut |
|---|---|
| `HasStoredToken` | true, wenn der Speicher eine lesbare Anmeldung enthält. Es fragt nicht bei iNaturalist nach: Ein abgelaufenes API-Token zählt auch (die nächste Anfrage erneuert es), ebenso eine Anmeldung, deren Zugriff die Person widerrufen hat, bis die Erneuerung abgelehnt wird. |
| `StoredUsername` | Der iNaturalist-Login der gespeicherten Person, für eine Anzeige "verbunden als ...", oder null. |
| `LoginAsync()` | "Verbinden": führt die Browser-Anmeldung aus und speichert das Ergebnis; eine gespeicherte Anmeldung wird ersetzt. Wirft, wenn sie scheitert (`INaturalistLoginException`, `PlatformNotSupportedException`, `INaturalistApiException`; `OperationCanceledException` bei Abbruch). |
| `Logout()` | "Trennen": löscht das gespeicherte Token. Den Zugriff der Anwendung bei iNaturalist widerruft es nicht; das geschieht in den Kontoeinstellungen von iNaturalist. Der nächste Lauf braucht eine neue Browser-Anmeldung. |

`HasStoredToken` und `StoredUsername` lesen bei jedem Zugriff den Speicher (beim Standardspeicher: die Datei lesen und unter Windows
entschlüsseln), also dann abfragen, wenn die Oberfläche sie braucht, nicht in einer Schleife.

## Grenzen und Regeln, die vor dem Veröffentlichen geprüft werden

Vor der Anmeldung und bevor etwas geschrieben wird, prüft der Publisher, was iNaturalist ablehnen würde, und meldet es als
`PublishStatus.SkippedInvalidEntry` (der Eintrag) oder `SkippedInvalidEvidence` (die Dateien) mit allen Problemen in
`PublishResult.Message`. Das gilt auch für einen Probelauf. Ohne diese Prüfung scheitert ein abgelehnter Upload erst, nachdem die
Beobachtung angelegt wurde, und hinterlässt eine öffentliche Beobachtung ohne Foto oder Ton. Die Regeln stammen aus dem
Open-Source-Code und dem Forum von iNaturalist, nicht aus einer offiziellen Referenz, und sind noch nicht am Live-Dienst geprüft.

| Regel | Grenze |
|---|---|
| Dateigröße (Spektrogramm und Audio, jeweils) | 20 MB (`INaturalistOptions.MaxEvidenceBytes`, Standard 20.000.000 Byte) |
| Beobachtungsdatum | nicht in der Zukunft (ein Zeitpunkt nach jetzt, wie bei der Eingabedatei), nicht älter als 130 Jahre |
| Breitengrad | eine Zahl größer als -90 und kleiner als 90 |
| Längengrad | eine Zahl von -180 bis 180 |
| Position | nicht genau 0, 0 |
| Artname (`species_guess`) | höchstens 255 Zeichen |
| `INaturalistOptions.TagList` | höchstens 750 Zeichen insgesamt, höchstens 255 pro Tag |

Einige Zeilen sind die plattformneutralen Prüfungen der Eingabedatei (siehe `docs/input-format.de.md`), die `ExportOrchestrator`
für jeden Kandidaten ausführt, bevor der Publisher ihn sieht. Die Regeln für den Eintrag kommen
zuerst: Scheitert eine, ist das Ergebnis `SkippedInvalidEntry`, und die Dateigrößen werden noch nicht geprüft.
iNaturalist lehnt außerdem ein Datum nach dem heutigen Tag in der Zeitzone des Kontos ab, die das Paket nicht kennt. Eine Aufnahme,
die nicht in der Zukunft liegt, kann diese Regel nur verletzen, wenn ihre eigene Zeitzone der des Kontos voraus ist; eine solche
Anfrage scheitert mit der Meldung von iNaturalist.

Unverändert akzeptiert: PNG-Fotos (iNaturalist skaliert sie auf 2048 Pixel an der längsten Seite) und WAV-Töne
(unverändert gespeichert; iNaturalist akzeptiert auch MP3 und M4A, das Eingabeformat kennt aber nur `.wav`).
Für Dauer oder Abtastrate nennt iNaturalist keine Grenze. Zur Orientierung: Eine Mono-Aufnahme mit 16 Bit bei 384 kHz braucht
768 kB pro Sekunde, 20 MB sind also nach etwa 26 Sekunden voll. Ist eine Datei zu groß, die Aufnahme kürzen oder die Abtastrate
senken und den Lauf wiederholen; für diesen Eintrag wurde nichts angelegt.

Fehlermeldungen, die iNaturalist selbst zurückschickt, werden unverändert in `PublishResult.Message` durchgereicht, auf die ersten 1000 Zeichen gekürzt
(`INaturalistApiException.ResponseBody` behält den ganzen Text; ein Serverfehler kann mit einer vollständigen HTML-Seite antworten).

## Anfragerate und Wiederholungen

iNaturalist drosselt die API bei 100 Anfragen pro Minute und bittet darum, bei 60 oder weniger zu bleiben; darüber antwortet es mit
`429 Too Many Requests`. Ein Kandidat braucht etwa fünf Anfragen (Taxon-Suche, Duplikatprüfung, Anlegen, Foto, Ton), dazu einen
Token-Austausch, wenn das gespeicherte API-Token abgelaufen ist. Das Paket taktet seine Anfragen deshalb und kann Fehler wiederholen.

**Taktung (standardmäßig an).** `INaturalistOptions.MinRequestInterval` (Standard eine Sekunde) ist die Mindestzeit zwischen den Starts
zweier Anfragen. Ein Lauf bleibt damit bei etwa 60 Anfragen pro Minute, ein Kandidat dauert etwa fünf Sekunden. Der Abstand gilt je
`INaturalistPublisher` (der Authenticator hat einen eigenen, er sendet selten): nicht mehrere Publisher parallel gegen ein Konto laufen lassen.
`TimeSpan.Zero` schaltet die Taktung ab. Das Paket zählt keine Anfragen; die empfohlene Vorgehensweise von iNaturalist nennt auch eine
Tagesmenge, eine sehr große Datei also auf mehrere Tage verteilen.

**Wiederholungen (standardmäßig aus).** `INaturalistOptions.MaxAttempts` ist die Zahl der Versuche pro Anfrage, der erste eingerechnet.
Der Standard 1 wiederholt nicht: Ein vorübergehender Fehler beendet den Kandidaten als `Failed`, und der nächste Lauf vervollständigt ihn
(siehe [Erneuter Lauf: Duplikate und Fortsetzen](inaturalist.de.md#erneuter-lauf-duplikate-und-fortsetzen)). Mit 3 wird wiederholt. Ein Fehler wird nur wiederholt, wenn dadurch nichts doppelt entstehen kann:

| Anfrage | Wiederholt nach | Nicht wiederholt nach |
|---|---|---|
| Lesen: Taxon-Suche, Duplikatprüfung, Benutzerprofil, Token-Austausch | 429, 500, 502, 503, 504, einem Netzwerkfehler, einem Timeout | jedem anderen Status (400, 401, 404, 422, ...) |
| Schreiben: Beobachtung anlegen, Spektrogramm anhängen, Audio anhängen | 429 (vor der Verarbeitung abgelehnt); einer Verbindung, die nicht aufgebaut werden konnte (kein Netz, DNS, TLS-Handshake) | 500, 502, 503, 504, einem Netzwerkfehler nach dem Senden, einem Timeout |

Eine Schreibanfrage, die so scheitert, kann schon verarbeitet worden sein; ein erneutes Senden könnte eine zweite Beobachtung oder einen
zweiten Upload anlegen. Sie endet als `Failed` (mit gesetzter `ObservationId`, wenn die Beobachtung existiert), und Duplikatprüfung und Fortsetzen im nächsten Lauf klären den Rest.

Die Wartezeit vor einer Wiederholung ist `RetryBaseDelay` (Standard 5 Sekunden), mit jeder weiteren Wiederholung verdoppelt, höchstens
`MaxRetryDelay` (Standard 60 Sekunden). Sendet iNaturalist einen `Retry-After`-Header (die Dokumentation verspricht keinen), gilt stattdessen diese Wartezeit.
Verlangt der Header mehr als `MaxRetryDelay`, wird nicht abgewartet: Die Anfrage scheitert sofort, und
`INaturalistApiException.RetryAfter` enthält die verlangte Zeit. Nach dem letzten Versuch wird der ursprüngliche Fehler unverändert gemeldet.
Jede Wiederholung wird als Warnung (`Warning`) protokolliert (Ereignis 3003, siehe [logging.de.md](logging.de.md)), ohne die URL, die Koordinaten enthalten kann.

Nicht wiederholt werden die Token-Anfrage der Browser-Anmeldung (ihr Code gilt nur einmal) und alles, was keine der genannten Anfragen ist.
Ein Abbruch während einer Wartezeit beendet den Kandidaten als `Cancelled`.

**Timeout beim Hochladen.** `HttpClient.Timeout` des an `INaturalistPublisher` übergebenen Clients (Standard 100 Sekunden) begrenzt
auch das Hochladen der Belege: Eine WAV-Datei mit 20 MB braucht etwa 1,6 Mbit/s im Upload, um rechtzeitig fertig zu werden. Ein
Upload mit Timeout ist eine Schreibanfrage und wird daher nicht wiederholt; der Kandidat endet als `Failed`, ein erneuter Lauf
vervollständigt ihn. Bei langsamen Leitungen einen `HttpClient` mit größerem `Timeout` übergeben.

## Fehlersuche

| Symptom | Ursache / Lösung |
|---|---|
| `ArgumentException` zu `ClientId` | `INaturalistOptions.ClientId` ist leer. |
| `INaturalistLoginException`, `Reason` = `ListenerUnavailable`, bei der Anmeldung | Port belegt und kein Ersatzport ließ sich öffnen, siehe [Port 45679](#port-45679). |
| iNaturalist-Fehlerseite nach "Authorize" | Die registrierte Redirect URI weicht von `INaturalistOptions.RedirectUri` ab. |
| `INaturalistLoginException`, `Reason` = `TimedOut`, bei der Anmeldung | Niemand hat die Browser-Anmeldung innerhalb von `AuthorizationTimeout` abgeschlossen. |
| Ein Kandidat nach dem anderen endet als `Failed` mit einer `INaturalistLoginException` in `Error`, und der Browser öffnet sich jedes Mal neu | Es war keine Anmeldung gespeichert, und die Browser-Anmeldung im Lauf wurde abgelehnt oder lief ab. Vor dem Lauf anmelden und ihn beim ersten solchen Ergebnis abbrechen, siehe [Wenn die Anmeldung während eines Laufs scheitert](#wenn-die-anmeldung-während-eines-laufs-scheitert). |
| `PlatformNotSupportedException` bei der Anmeldung oder beim Speichern des Tokens | Nicht Windows. Die Prüfung läuft, bevor sich der Browser öffnet. `allowPlaintextOnNonWindows: true` übergeben (Dateimodus `0600`, schützt nur vor anderen lokalen Benutzern) oder einen eigenen `INaturalistTokenStore` bereitstellen. |
| Anmeldung wird erneut verlangt, obwohl man sich schon angemeldet hatte | Die Token-Datei war nicht lesbar (beschädigt oder von einem anderen Windows-Benutzer verschlüsselt); eine Warnung wird protokolliert, die nächste Anmeldung ersetzt die Datei. Siehe [Token-Speicherung](#token-speicherung). |
| `Failed` mit `429` oder `Retry-After` in `INaturalistApiException` | iNaturalist hat den Lauf gedrosselt. `MinRequestInterval` erhöhen, `MaxAttempts` auf 3 setzen, siehe [Anfragerate und Wiederholungen](#anfragerate-und-wiederholungen); später erneut laufen lassen, der nächste Lauf vervollständigt den Rest. |
| `Failed` mit einem 5xx oder einem Netzwerkfehler | Vorübergehend, bei iNaturalist oder auf der eigenen Seite. Erneut laufen lassen: Duplikatprüfung und Fortsetzen vervollständigen den Eintrag. |
| 401/403 beim JWT-Austausch | Siehe [Warum es einen zusätzlichen JWT-Austausch gibt](#warum-es-einen-zusätzlichen-jwt-austausch-gibt); Registrierung der Anwendung unter <https://www.inaturalist.org/oauth/applications> prüfen. |
