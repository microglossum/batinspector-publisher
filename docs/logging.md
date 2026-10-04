# Logging

German version: [logging.de.md](logging.de.md)

The library never writes to the console or to files. It logs through `Microsoft.Extensions.Logging.Abstractions`
(`ILogger`), and the host decides where the messages go. Every public class that logs takes an optional logger
(`ExportOrchestrator`, `INaturalistPublisher`, `INaturalistAuthenticator`, `ProtectedFileTokenStore`); without one it logs nothing.

## Levels

| Level | What | Examples |
| --- | --- | --- |
| Debug | Every HTTP call to iNaturalist: method, URL, status and the response body (truncated to 2000 characters) | `GET .../observations?... -> 200; body: {...}` |
| Information | Outcomes | one line per candidate and status, a created or completed observation, a login |
| Warning | A fallback the host may want to know about | the stored token was rejected, the token file is unreadable, the login port was taken |
| Error | Only the unexpected: a candidate that ended as `Failed`, logged with its exception | `inaturalist: Pipistrellus pipistrellus @ ... failed: ...` |

A skipped entry (duplicate, unresolved taxon, invalid evidence) and a cancelled candidate are expected outcomes and log at Information,
with the status and the reason.

## What is never logged

- Tokens: the bodies of the OAuth token endpoint and of the token exchange are not logged, and no request headers are logged.
  A test (`LoggingTests`) runs a login with a capturing logger and checks that no token appears at any level.
- At Debug, URLs and bodies can contain **observation data** (coordinates, species, the description text). Turn Debug on for diagnosing,
  not by default, and treat such logs as data.
- An error from iNaturalist is passed through (decided): its response body is part of the exception message and therefore of the
  `Error` line and of `PublishResult.Message`.

## Event IDs

Every message has a fixed event ID and name, so a host can filter or alert on them. They do not change within a major version.

| Range | Class | Events |
| --- | --- | --- |
| 1000 | `ExportOrchestrator` | 1001 `CandidateProcessed`, 1002 `CandidateFailed` |
| 2000 | `INaturalistAuthenticator` | 2001 `StoredTokenRejected`, 2002 `TokenRefreshFailed`, 2003 `LoggedIn` |
| 2100 | OAuth login | 2101 `OAuthTokenRequest`, 2102 `OAuthCannotListen`, 2103 `OAuthFallbackPort`, 2104 `OAuthIgnoredRequest` |
| 2200 | `ProtectedFileTokenStore` | 2201 `TokenFileUnreadable` |
| 3000 | iNaturalist REST calls | 3001 `HttpCall`, 3002 `NoNumericObservationId` |
| 4000 | `INaturalistPublisher` | 4001 `ObservationResuming`, 4002 `ObservationCreated` |

## Plugging in a logger

With dependency injection the container supplies `ILogger<T>` for the classes above. Note that `ProtectedFileTokenStore`
takes a plain `ILogger`; pass one yourself.

Without dependency injection (BatInspector), create a factory once and hand out loggers:

```csharp
// package Microsoft.Extensions.Logging.Console
using var factory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole()
    .SetMinimumLevel(LogLevel.Information));

var orchestrator = new ExportOrchestrator(publisher, factory.CreateLogger<ExportOrchestrator>());
var authenticator = new INaturalistAuthenticator(options, http, logger: factory.CreateLogger<INaturalistAuthenticator>());
```

For a log file, use a provider that writes one, for example Serilog (packages `Serilog.Extensions.Logging` and `Serilog.Sinks.File`):

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File("publisher-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

using var factory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: true));
```

To show progress in a UI, use the `IProgress<PublishResult>` parameter of `ExportOrchestrator.RunAsync`, not the log: logs are for
diagnosis, progress is for the user.
