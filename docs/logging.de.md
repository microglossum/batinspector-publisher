# Logging

English version: [logging.md](logging.md)

Die Bibliothek schreibt weder auf die Konsole noch in Dateien. Sie loggt über `Microsoft.Extensions.Logging.Abstractions`
(`ILogger`); wohin die Meldungen gehen, entscheidet die Anwendung. Jede öffentliche Klasse, die loggt, nimmt einen optionalen Logger
(`ExportOrchestrator`, `INaturalistPublisher`, `INaturalistAuthenticator`, `ProtectedFileTokenStore`); ohne Logger wird nichts protokolliert.

## Stufen

| Stufe | Was | Beispiele |
| --- | --- | --- |
| Debug | Jeder HTTP-Aufruf an iNaturalist: Methode, URL, Status und der Antworttext (auf 2000 Zeichen gekürzt) | `GET .../observations?... -> 200; body: {...}` |
| Information | Ergebnisse | eine Zeile je Kandidat und Status, eine angelegte oder vervollständigte Beobachtung, eine Anmeldung |
| Warning | Ein Ausweichverhalten, das die Anwendung kennen sollte | das gespeicherte Token wurde abgelehnt, die Token-Datei ist unlesbar, der Anmelde-Port war belegt, eine Anfrage wird wiederholt |
| Error | Nur Unerwartetes: ein Kandidat, der als `Failed` endete, mit seiner Ausnahme | `inaturalist: Pipistrellus pipistrellus @ ... failed: ...` |

Ein übersprungener Eintrag (Duplikat, nicht aufgelöstes Taxon, ungültige Belege) und ein abgebrochener Kandidat sind erwartete Ergebnisse
und werden als Information mit Status und Grund protokolliert.

## Was nie protokolliert wird

- Tokens: Die Antworttexte des OAuth-Token-Endpunkts und des Token-Tauschs werden nicht protokolliert, und es werden keine Request-Header protokolliert.
  Ein Test (`LoggingTests`) führt eine Anmeldung mit einem mitschreibenden Logger aus und prüft, dass auf keiner Stufe ein Token vorkommt.
- Auf Debug können URLs und Antworttexte **Beobachtungsdaten** enthalten (Koordinaten, Art, Beschreibungstext). Debug nur zur Fehlersuche
  einschalten, nicht dauerhaft, und solche Logs wie Daten behandeln.
- Eine Fehlermeldung von iNaturalist wird unverändert durchgereicht (so entschieden): Ihr Antworttext ist Teil der Ausnahmemeldung und damit
  der `Error`-Zeile und von `PublishResult.Message`, auf die ersten 1000 Zeichen gekürzt (`INaturalistApiException.ResponseBody` behält alles).
- Die Warnung zur Wiederholung (`RequestRetrying`) nennt Vorgang, Grund und Wartezeit, nie die URL (sie kann Koordinaten enthalten) oder den Antworttext.

## Ereignis-IDs

Jede Meldung hat eine feste Ereignis-ID und einen Namen, damit die Anwendung danach filtern oder alarmieren kann. Sie ändern sich innerhalb einer Hauptversion nicht.

| Bereich | Klasse | Ereignisse |
| --- | --- | --- |
| 1000 | `ExportOrchestrator` | 1001 `CandidateProcessed`, 1002 `CandidateFailed` |
| 2000 | `INaturalistAuthenticator` | 2001 `StoredTokenRejected`, 2002 `TokenRefreshFailed`, 2003 `LoggedIn` |
| 2100 | OAuth-Anmeldung | 2101 `OAuthTokenRequest`, 2102 `OAuthCannotListen`, 2103 `OAuthFallbackPort` |
| 2200 | `ProtectedFileTokenStore` | 2201 `TokenFileUnreadable` |
| 3000 | iNaturalist-REST-Aufrufe | 3001 `HttpCall`, 3002 `NoNumericObservationId`, 3003 `RequestRetrying` |
| 4000 | `INaturalistPublisher` | 4001 `ObservationResuming`, 4002 `ObservationCreated` |

## Einen Logger anschließen

Mit Dependency Injection liefert der Container `ILogger<T>` für die genannten Klassen. `ProtectedFileTokenStore` nimmt einen
einfachen `ILogger`; den übergibt man selbst.

Ohne Dependency Injection (BatInspector) erzeugt man einmal eine Factory und holt sich daraus die Logger:

```csharp
// Paket Microsoft.Extensions.Logging.Console
using var factory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole()
    .SetMinimumLevel(LogLevel.Information));

var orchestrator = new ExportOrchestrator(publisher, factory.CreateLogger<ExportOrchestrator>());
var authenticator = new INaturalistAuthenticator(options, http, logger: factory.CreateLogger<INaturalistAuthenticator>());
```

Für eine Logdatei nimmt man einen Provider, der eine schreibt, zum Beispiel Serilog (Pakete `Serilog.Extensions.Logging` und `Serilog.Sinks.File`):

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File("publisher-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

using var factory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: true));
```

Für den Fortschritt in einer Oberfläche dient der Parameter `IProgress<PublishResult>` von `ExportOrchestrator.RunAsync`, nicht das Log:
Logs sind für die Diagnose, der Fortschritt ist für den Benutzer.
