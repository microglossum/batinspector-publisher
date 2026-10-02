# Changelog

All notable changes are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [Unreleased]

### Added

- Targets net8.0 and net10.0.
- Core: `ObservationCandidate`, `IObservationPublisher`, `ExportOrchestrator`, `PublishResult`.
- Input schema v1 reader (`InputSchemaReader`) with required `SchemaVersion`.
- iNaturalist adapter: OAuth (Authorization Code + PKCE), token store, taxon resolution, duplicate check, observation creation with spectrogram and audio evidence.
