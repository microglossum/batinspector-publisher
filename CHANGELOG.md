# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]

### Added

- Targets net8.0 and net10.0.
- Core: `ObservationCandidate`, `IObservationPublisher`, `ExportOrchestrator`, `PublishResult`.
- Input schema v1 reader (`InputSchemaReader`) with required `SchemaVersion`.
- Per-entry input validation: evidence paths must be absolute and end in `.png` / `.wav`, latitude and longitude both 0 and dates in the future are rejected. A bad entry is left out of `InputDocument.Candidates` and reported in `InputDocument.Rejected`; the valid entries are still returned.
- `ObservationCandidate.ObservedAt` is a `DateTimeOffset`. The input time is read as Europe/Berlin local time with the right offset; a time in the spring-forward gap rejects the entry, the repeated autumn hour is read as standard time, and missing time zone data throws `TimeZoneNotFoundException` instead of guessing. The future check compares instants; the duplicate check and `observed_on_string` use the local calendar day. The German description names the zone (MEZ/MESZ).
- Optional `TimeZone` (IANA id) per input entry, an additive field of schema v1: the `Date` is read in that zone instead of Europe/Berlin (`ObservationCandidate.TimeZoneId`). New `InputDocument.Warnings` (accepted but doubtful entries); the first warning is the ambiguous repeated autumn hour. The German description names a non-Berlin zone explicitly.
- Evidence is read once per candidate, checked (not empty, PNG / WAV signature) and uploaded as the checked bytes. New `PublishStatus.SkippedInvalidEvidence`; `IObservationPublisher.PublishAsync` takes the loaded `EvidenceFiles`.
- Cancellation no longer loses work: new `PublishStatus.Cancelled` carries the observation id, the attached flags and the interrupted stage (`PublishResult.InterruptedStep`, also set on `Failed`). `ExportOrchestrator.RunAsync` returns the results so far instead of throwing `OperationCanceledException`. An `OperationCanceledException` that is not the caller's (an HTTP timeout) is a `Failed` result, not an aborted run.
- Re-running completes a partial observation: when the duplicate check finds an observation with the same description as the entry that still lacks its spectrogram or audio, the missing evidence is attached and the result is the new `PublishStatus.Resumed`. Other existing observations are never touched; nothing is deleted. `SkippedDuplicate` now also reports the existing observation id.
- The iNaturalist publisher logs the id of a created observation at Information right after creation.
- OAuth login falls back to up to three OS-assigned loopback ports when the configured redirect port is in use, and uses the port it actually bound in the authorize request and the token exchange. iNaturalist ignores the port of a loopback redirect URI, so the one registered URI is enough. If no port can be opened, the login throws `InvalidOperationException` instead of `HttpListenerException`.
- iNaturalist adapter: OAuth (Authorization Code + PKCE), token store, taxon resolution, duplicate check, observation creation with spectrogram and audio evidence.
