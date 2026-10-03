# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]

### Added

- Targets net8.0 and net10.0.
- Core: `ObservationCandidate`, `IObservationPublisher`, `ExportOrchestrator`, `PublishResult`.
- Input schema v1 reader (`InputSchemaReader`) with required `SchemaVersion`.
- Per-entry input validation: evidence paths must be absolute and end in `.png` / `.wav`, latitude and longitude both 0 and dates in the future are rejected. A bad entry is left out of `InputDocument.Candidates` and reported in `InputDocument.Rejected`; the valid entries are still returned.
- Evidence is read once per candidate, checked (not empty, PNG / WAV signature) and uploaded as the checked bytes. New `PublishStatus.SkippedInvalidEvidence`; `IObservationPublisher.PublishAsync` takes the loaded `EvidenceFiles`.
- iNaturalist adapter: OAuth (Authorization Code + PKCE), token store, taxon resolution, duplicate check, observation creation with spectrogram and audio evidence.
