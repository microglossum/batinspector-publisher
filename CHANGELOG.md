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
- iNaturalist adapter: OAuth (Authorization Code + PKCE), token store, taxon resolution, duplicate check, observation creation with spectrogram and audio evidence.
