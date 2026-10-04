# iNaturalist-Einrichtung

English version: [inaturalist-setup.md](inaturalist-setup.md)

BatInspectorPublisher liefert keine iNaturalist-Zugangsdaten mit. Die Anwendung, die das Paket
nutzt, registriert einmal eine eigene OAuth-Anwendung und übergibt die Werte über `INaturalistOptions`. **Das Registrieren einer Anwendung ist bei iNaturalist beschränkt, siehe den ersten Abschnitt.**

## Voraussetzung: Zulassung als "App Owner"

**Eine iNaturalist-Anwendung anzulegen, geht nicht einfach so.** iNaturalist lässt nicht jeden eine Anwendung registrieren.
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
dort wird die Antragsseite und Mitarbeitende von iNaturalist zitiert. Die Bedingungen kann iNaturalist ändern; den aktuellen Text auf der Antragsseite prüfen.

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
* **Öffentlich**: Eine native Desktop-Anwendung kann kein Secret sicher aufbewahren, das ist die technisch richtige Wahl.
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

`EnsureAuthenticatedAsync` (vom Publisher aufgerufen) liefert das gespeicherte API-Token und erneuert es still, wenn es abgelaufen ist:
OAuth-Tokens von iNaturalist laufen nie ab, die Browser-Anmeldung ist also **einmal** nötig (bis die Person den Zugriff widerruft). Nur wenn iNaturalist das gespeicherte Token ablehnt, folgt eine neue Anmeldung. Ein Netzwerkfehler startet nie eine Anmeldung.

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
| Abgeschottete oder paketierte Anwendungen mit Loopback-Isolierung (zum Beispiel MSIX) | Die Anwendung erreicht ihren eigenen Loopback-Listener vom Browser aus eventuell nicht. | Nicht unterstützt: die Anmeldung braucht einen Loopback-Listener. |

Eine fehlgeschlagene Anmeldung speichert nichts. Abbrechen und neu versuchen; die Anfragen anderer Programme beenden das Warten nicht.

### Warum es einen zusätzlichen JWT-Austausch gibt

Die REST-API von iNaturalist akzeptiert das rohe OAuth-Access-Token nicht als Bearer-Token (401). Das Token wird
zuerst per `GET https://www.inaturalist.org/users/api_token` (Host `www`) gegen ein JWT getauscht. Dieses JWT,
etwa 24 Stunden gültig, dient für alle API-Aufrufe und wird lokal gespeichert. Das steht nicht in der offiziellen API-Referenz.

## Grenzen und Regeln, die vor dem Veröffentlichen geprüft werden

Vor der Anmeldung und bevor etwas geschrieben wird, prüft der Publisher, was iNaturalist ablehnen würde, und meldet es als
`PublishStatus.SkippedInvalidEntry` (der Eintrag) oder `SkippedInvalidEvidence` (die Dateien) mit allen Problemen in
`PublishResult.Message`. Das gilt auch für einen Trockenlauf. Ohne diese Prüfung scheitert ein abgelehnter Upload erst, nachdem die
Beobachtung angelegt wurde, und hinterlässt eine öffentliche Beobachtung ohne Foto oder Ton. Die Regeln stammen aus dem
Open-Source-Code und dem Forum von iNaturalist, nicht aus einer offiziellen Referenz, und sind noch nicht am Live-Dienst geprüft.

| Regel | Grenze |
|---|---|
| Dateigröße (Spektrogramm und Audio, jeweils) | 20 MB (`INaturalistOptions.MaxEvidenceBytes`, Standard 20.000.000 Byte) |
| Beobachtungsdatum | nicht in der Zukunft (mit der Toleranz aller Zeitzonen geprüft), nicht älter als 130 Jahre |
| Breitengrad | größer als -90 und kleiner als 90 |
| Längengrad | -180 bis 180 |
| Position | nicht genau 0, 0 |
| Artname (`species_guess`) | höchstens 255 Zeichen |
| `INaturalistOptions.TagList` | höchstens 750 Zeichen insgesamt, höchstens 255 pro Tag |

Unverändert akzeptiert: PNG-Fotos (iNaturalist skaliert sie auf 2048 Pixel an der längsten Seite) und WAV-Töne
(unverändert gespeichert; iNaturalist akzeptiert auch MP3 und M4A, das Eingabeformat kennt aber nur `.wav`).
Für Dauer oder Abtastrate nennt iNaturalist keine Grenze. Zur Orientierung: Eine Mono-Aufnahme mit 16 Bit bei 384 kHz braucht
768 kB pro Sekunde, 20 MB sind also nach etwa 26 Sekunden voll. Ist eine Datei zu groß, die Aufnahme kürzen oder die Abtastrate
senken und den Lauf wiederholen; für diesen Eintrag wurde nichts angelegt.

Fehlermeldungen, die iNaturalist selbst zurückschickt, werden unverändert in `PublishResult.Message` durchgereicht.

## Fehlersuche

| Symptom | Ursache / Lösung |
|---|---|
| `ArgumentException` zu `ClientId` | `INaturalistOptions.ClientId` ist leer. |
| `INaturalistLoginException`, `Reason` = `ListenerUnavailable`, bei der Anmeldung | Port belegt und kein Ersatzport ließ sich öffnen, siehe [Port 45679](#port-45679). |
| iNaturalist-Fehlerseite nach "Authorize" | Die registrierte Redirect URI weicht von `INaturalistOptions.RedirectUri` ab. |
| `INaturalistLoginException`, `Reason` = `TimedOut`, bei der Anmeldung | Niemand hat die Browser-Anmeldung innerhalb von `AuthorizationTimeout` abgeschlossen. |
| `PlatformNotSupportedException` bei der Anmeldung oder beim Speichern des Tokens | Nicht Windows. Die Prüfung läuft, bevor sich der Browser öffnet. `allowPlaintextOnNonWindows: true` übergeben (Dateimodus `0600`, schützt nur vor anderen lokalen Benutzern) oder einen eigenen `INaturalistTokenStore` bereitstellen. |
| Anmeldung wird erneut verlangt, obwohl man sich schon angemeldet hatte | Die Token-Datei war nicht lesbar (beschädigt oder von einem anderen Windows-Benutzer verschlüsselt); eine Warnung wird protokolliert, die nächste Anmeldung ersetzt die Datei. |
| 401/403 beim JWT-Austausch | Siehe [Warum es einen zusätzlichen JWT-Austausch gibt](#warum-es-einen-zusätzlichen-jwt-austausch-gibt); Registrierung der Anwendung unter <https://www.inaturalist.org/oauth/applications> prüfen. |
