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
ein temporärer lokaler Listener fängt diese eine Anfrage ab. `AuthorizationTimeout` (Standard 5 Minuten) begrenzt die Wartezeit.

`EnsureAuthenticatedAsync` (vom Publisher aufgerufen) liefert das gespeicherte API-Token und erneuert es still, wenn es abgelaufen ist:
OAuth-Tokens von iNaturalist laufen nie ab, die Browser-Anmeldung ist also **einmal** nötig (bis die Person den Zugriff widerruft). Nur wenn iNaturalist das gespeicherte Token ablehnt, folgt eine neue Anmeldung. Ein Netzwerkfehler startet nie eine Anmeldung.

### Port 45679

iNaturalist verlangt, dass die Redirect URI jeder Anfrage exakt einem registrierten Wert entspricht, und
es ist nicht bestätigt, dass beliebige Loopback-Ports akzeptiert werden. Der Port ist daher fest. 45679
kollidiert nicht mit üblichen Entwicklungsservern. Ist er auf einem Rechner belegt, schlägt die Anmeldung mit
einer `HttpListenerException` fehl. Dann einen anderen Port wählen, `INaturalistOptions.RedirectUri`
setzen und dieselbe URI bei iNaturalist registrieren.

Der Host muss an beiden Stellen `127.0.0.1` sein: `HttpListener` vergleicht den `Host`-Header wörtlich, ein
Listener auf `127.0.0.1` antwortet bei `localhost` mit 404.

### Warum es einen zusätzlichen JWT-Austausch gibt

Die REST-API von iNaturalist akzeptiert das rohe OAuth-Access-Token nicht als Bearer-Token (401). Das Token wird
zuerst per `GET https://www.inaturalist.org/users/api_token` (Host `www`) gegen ein JWT getauscht. Dieses JWT,
etwa 24 Stunden gültig, dient für alle API-Aufrufe und wird lokal gespeichert. Das steht nicht in der offiziellen API-Referenz.

## Fehlersuche

| Symptom | Ursache / Lösung |
|---|---|
| `ArgumentException` zu `ClientId` | `INaturalistOptions.ClientId` ist leer. |
| `HttpListenerException` bei der Anmeldung | Port belegt, siehe [Port 45679](#port-45679). |
| iNaturalist-Fehlerseite nach "Authorize" | Die registrierte Redirect URI weicht von `INaturalistOptions.RedirectUri` ab. |
| `TimeoutException` bei der Anmeldung | Niemand hat die Browser-Anmeldung innerhalb von `AuthorizationTimeout` abgeschlossen. |
| `PlatformNotSupportedException` beim Speichern des Tokens | Nicht Windows. `allowPlaintextOnNonWindows: true` übergeben oder einen eigenen `INaturalistTokenStore` bereitstellen. |
| 401/403 beim JWT-Austausch | Siehe [Warum es einen zusätzlichen JWT-Austausch gibt](#warum-es-einen-zusätzlichen-jwt-austausch-gibt); Registrierung der Anwendung unter <https://www.inaturalist.org/oauth/applications> prüfen. |
