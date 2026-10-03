# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
Workflow: read this before starting work; when an item is done move it to **Done** with the date and add a
`CHANGELOG.md` line if users of the package notice it. Decisions already taken live in
`CLAUDE.md`.

## Open

### Cross-platform token storage

BatInspector is Windows-only today, so `ProtectedFileTokenStore` (DPAPI) is enough. On other platforms it refuses
to write the iNaturalist token unless `allowPlaintextOnNonWindows: true` is passed. When BatInspector becomes
OS-independent, the local secret storage needs a real answer per OS:

- macOS Keychain, Linux Secret Service / libsecret (or kwallet), Windows DPAPI or Credential Manager; or a cross-platform
  library; or leave it to the host through the existing `INaturalistTokenStore` seam.
- Decided direction (2026-10-03): leave real secure storage to the host through `INaturalistTokenStore`, and ship a documented minimum for the opt-in plaintext case:
  create the token file with owner-only permissions (`UnixCreateMode = UserRead | UserWrite`, mode `0600`) and write it atomically (temp file, then move).
  Mention in the docs that this protects against other local users only, not against other processes of the same user. A Keychain / Secret Service package is deferred until BatInspector runs off Windows.
- The stored OAuth token never expires, so it is a long-lived credential for the user's iNaturalist account (unlike the client secret, which a distributed app cannot keep confidential).
- Do not design a shared "secure value store" abstraction from one adapter. Revisit once a second adapter
  (naturgucker) shows what it needs to store.
- See also "Token store hardening" for fail-fast on stores that cannot save and for corrupt token files.
- The host's own OAuth client ID/secret settings need a cross-OS home too, but that is the host's concern.
- Needs a Linux/macOS CI leg that exercises the new store.

### Rethink taxon matching

Today: exact case-insensitive name match on `/v2/taxa/autocomplete`; no match means `SkippedUnresolvedTaxon`; there is no
fallback to the first hit (that would file observations under wrong species). Open questions:

- iNaturalist taxonomy versus German/EU bat checklists: synonyms, splits and lumps (Myotis mystacinus/brandtii,
  Plecotus, ...). Does autocomplete return inactive or synonym taxa?
- Genus-level and uncertain calls (BatInspector codes like "Nyctaloid" or "?"): how will they appear in `SpeciesLatin`,
  and what should happen? Rank checks?
- Several exact matches, a host-supplied name override map, regression fixtures from real responses.
Decide the expected behavior with real BatInspector data before changing code.

### Rethink part-uploads

Today: create observation, then attach spectrogram, then audio. If an attachment fails the result is `Failed` with
`ObservationId` and the attached flags set. Nothing retries or cleans up, and the next run's duplicate check finds the
half-created observation and reports `SkippedDuplicate`, so its evidence is never completed. Options to evaluate:

- resume: attach missing evidence to an existing observation id; make the duplicate check aware of missing evidence;
- roll back: delete the observation if evidence cannot be attached (check what the API allows);
- retry with backoff for transient errors, and rate limiting against iNaturalist's API etiquette;
- upload order, and what `PublishResult` should say about partial states (ties into a versioned result schema).

### Dry run should preview duplicates

`INaturalistPublisher` returns `WouldCreate` before the duplicate check, so a dry run reports "would create" for observations that a commit run skips as `SkippedDuplicate`.
The dry run is the default and the only safety net, so it should be faithful:

- Run the (read-only) duplicate check in the dry run too and report `SkippedDuplicate`.
- A dry run currently also needs a login for the JWT, although taxon autocomplete is a public endpoint. Check which calls really need auth; the duplicate check with `mine_only` does, so a dry run that previews duplicates needs the login anyway. Decide whether that is acceptable.

### Cancellation and partial state

- `OperationCanceledException` is rethrown by `INaturalistPublisher` and `ExportOrchestrator.RunAsync` discards the whole result list. A cancel after the observation was created loses its ID,
  and the next run reports `SkippedDuplicate` without ever completing the evidence. Hosts that do not use `IProgress<PublishResult>` lose everything that was already published.
- Decided (2026-10-03): add a `Cancelled` status to `PublishStatus`. The cancelled result carries everything that is known about what was already created, as detailed as possible
  (observation ID and URL, which evidence was attached, the step that was interrupted). Still to design: whether `RunAsync` then returns the results so far instead of throwing, and the exact payload shape.
- Independent of that: `INaturalistPublisher` should log the created observation ID at Information right after creation, so a trail exists even if everything after it fails.
- Ties into "Rethink part-uploads" (resume) and the versioned result schema.

### Token store hardening

Small, concrete fixes next to "Cross-platform token storage":

- Fail fast: `INaturalistAuthenticator` should refuse a store that cannot save before it opens the browser. Today `LoginCoreAsync` runs the whole login and only then `_store.Save` throws
  `PlatformNotSupportedException` on non-Windows, so the user authorizes for nothing. Options: a `CanSave` check on the store, or a check in the authenticator constructor.
- `ProtectedFileTokenStore.Load` throws on a corrupt or undecryptable file (crash during write, other Windows user). Every candidate then fails until `Logout()`. Treat an unreadable file as "no token" and log a warning.
- Owner-only file mode and atomic write: see "Cross-platform token storage".
- Tests: file mode on Linux/macOS, corrupt file, interrupted write.

### GitHub Actions

`.github/workflows/ci.yml` ran green on the Dependabot PRs (2026-10-02: Ubuntu and Windows build/test, format, pack, gitleaks). To do:

- `tech/setup-repo` is currently the default branch and `main` does not exist on the remote, so Dependabot PRs target the working branch. Push `main`, make it the default, let Dependabot retarget;
- the devcontainer node feature bump (1 -> 2) is still open as a Dependabot PR: rebuild the devcontainer locally before merging, CI does not cover it;
- add a CI job that runs `scripts/validate-config.sh` (the tools are not on the runners yet);
- release workflow per `docs/releasing.md`: checkout with `fetch-depth: 0` (MinVer reads the tag), pack, push to nuget.org, create the GitHub Release with notes extracted from `CHANGELOG.md`;
- branch protection on `main`, Dependabot for NuGet and Actions.

### First NuGet release

The package ID `BatInspectorPublisher` was free on nuget.org on 2026-10-02. Steps:

1. Create a nuget.org account (sign in with a Microsoft account) and enable 2FA.
2. Optional: reserve the ID prefix `BatInspector*` (free request, gives the verified badge).
3. Create an API key with Push scope, restricted to the glob `BatInspectorPublisher*`, with an expiry date.
   Alternative: nuget.org trusted publishing via GitHub OIDC, which avoids a long-lived key (verify current availability).
4. Store the key as the GitHub secret `NUGET_API_KEY`.
5. Tag-triggered release workflow, see `docs/releasing.md`.
6. First release as `0.1.0-preview.N`. Versions are immutable: they can be unlisted but not deleted.
Prerequisites: live smoke test (below), CHANGELOG, README check.

### Logging strategy

Today every class takes an optional `ILogger` (`Microsoft.Extensions.Logging.Abstractions`; `NullLogger` when omitted), so a host can plug in any logger.
Logged now: HTTP calls at Debug with truncated response bodies (the token exchange body is never logged), per-candidate results and logins at Information,
fallbacks (rejected token, failed refresh) at Warning. `INaturalistPublisher` itself logs nothing yet.

How .NET libraries usually do it (confirm while researching): depend only on the `Microsoft.Extensions.Logging.Abstractions` package (`ILogger<T>` / `ILoggerFactory`)
and let the host choose the provider; Serilog and NLog both bridge to it. Hosts without dependency injection create a factory (`LoggerFactory.Create(...)`, package
`Microsoft.Extensions.Logging`) or pass nothing. Older libraries expose their own event or callback; telemetry-minded ones add `ActivitySource` / metrics (OpenTelemetry).

Open decisions:

- Keep the optional `ILogger` constructor parameters, or accept one `ILoggerFactory`/options object that is passed once?
- `[LoggerMessage]` source-generated logging (fast, structured, event IDs) versus plain calls.
- Stable event IDs and message templates: they become part of what hosts can filter and alert on.
- What may be logged: never tokens or secrets (a test with a capturing logger should prove it). Decide about Debug response bodies (they can contain observation data such as coordinates), file paths and species.
  Define the level policy (Debug: HTTP; Information: outcomes; Warning: fallbacks; Error: only the unexpected).
- `INaturalistApiException.Message` contains the response body and ends up in `PublishResult.Message` and host UIs. Decide whether error bodies may be shown there or only kept in `ResponseBody`.
- Document how a host without DI (BatInspector) plugs in a logger, with a console and a file example.
- Keep logs (diagnostics) separate from progress for a UI (`IProgress<PublishResult>`).
- Optional later: `ActivitySource` and metrics.

### Research: how to build really good NuGet packages

Before the first release, research current best practice for publishing a high-quality package, then decide what to adopt. Starting points and topics:

- Microsoft's guidance: the .NET library guidance (learn.microsoft.com/dotnet/standard/library-guidance) and NuGet's package authoring best practices.
- Metadata and discoverability: README rendering on nuget.org, package icon, tags, description, license expression, release notes link, repository and SourceLink (done: MinVer, SourceLink, snupkg, deterministic CI builds).
- API quality: public API tracking (`Microsoft.CodeAnalysis.PublicApiAnalyzers`), package validation with a baseline version (`EnablePackageValidation`) to catch breaking changes automatically, nullable annotations, XML docs coverage.
- Compatibility: multi-targeting choices, minimum dependency versions, trimming and AOT annotations, minimal dependencies.
- Supply chain: package signing, nuget.org trusted publishing (OIDC) instead of long-lived API keys, NuGet audit, SBOM, dependency update policy (Dependabot is set up).
- Documentation: a docs site (DocFX on GitHub Pages) versus README only, samples, how a consumer discovers the BatInspector integration.
- Release hygiene: pre-release flow (`-preview.N`, `-rc.N`), deprecation and unlisting policy, prefix reservation.
- Look at well-regarded small packages and copy what works.

Output: a short decision list in this file (adopt, later, skip) and the resulting items in CI and the csproj.

### OAuth login robustness (loopback listener)

The login needs a local HTTP listener on `127.0.0.1` (RFC 8252 loopback redirect), which can fail where the host cannot open a port: port already in use,
two sessions on one machine (terminal server), locked-down machines, a browser on another machine than the app (remote/VDI/SSH), sandboxed or packaged apps with loopback isolation.
Facts found in iNaturalist's open source (Doorkeeper 5.6.6 on `main`, checked 2026-10-02; **not yet verified live**):

- OAuth access tokens never expire and no refresh tokens are issued, so the login is needed once. Done: the OAuth token is now stored and silently re-exchanged for the daily API JWT.
- For loopback IP redirect URIs (`127.0.0.1`, `::1`, not `localhost`) the **port is ignored** when matching, and the registered redirect URI field accepts several URIs (one per line).
  So an OS-assigned port should work with the one registered URI. Other apps use custom schemes (`myapp://callback`), so those are accepted too.

To do:

- Live test (needs the approved app): does an ephemeral port work? If yes, bind a free OS-assigned port (no collisions) and keep the fixed port as fallback.
- A host-pluggable receiver for the authorization response, next to `AuthorizationPrompt`: a manual "paste the redirected URL" fallback for blocked or remote environments, and a custom-scheme receiver for packaged desktop apps.
- Distinct, actionable error types: port in use, timeout, denied by user, token rejected.
- `OAuthFlow.WaitForAuthorizationCodeAsync` accepts only the first request on the listener. Any stray request (port scan, browser prefetch, another local program) ends the login with an error. Keep listening until a request carries a valid `state` or the timeout hits.
- Document the environments where the loopback login does not work and what to do.

### Input validation: remaining work

Principles for any new check: validate at the boundary, report every problem with its path, never alter data silently (except documented normalization such as species capitalization).
Two levels: **error** (the entry is rejected) and **warning** (reported in `InputDocument.Warnings`, does not block). Publishing is public and irreversible, so anything doubtful that cannot be fixed afterwards is an error, not a warning.
Platform limits belong in the adapter's pre-flight, not in `Core`.

- Rejected entries have no `ObservationCandidate`, so they are not part of `PublishResult`s; the host has to show `InputDocument.Rejected` itself. Decide whether a combined report is worth it.
- `Candidates` can be shorter than `DocumentFiles` and a candidate does not know its position in the file. If a host needs to map results back to entries, add the entry index to `ObservationCandidate`.
- Warnings exist now (`InputDocument.Warnings`, first user: ambiguous autumn hour). More to add: implausible temperature or humidity (omit the value from the description); a daytime timestamp for a bat; duplicate entries (same species, time and place; the remote duplicate check can lag); a name that is neither a binomial nor a genus.
- The signature checks are not a full format validation (a file can start like a PNG and still be something else); decide whether that is enough.

### Times and time zones: remaining

The offline part is done (2026-10-03, see Done). What is left needs the live test:

- **iNaturalist:** send the time as well, with the zone (the candidate now carries `TimeZoneId`), so the platform does not read it in the account's zone. Find out in the live test whether v2 accepts a time in `observed_on_string`, which zone parameter it expects (and in which name format),
  and whether `time_observed_at` is really rejected. Then update the README sentence "iNaturalist receives only the calendar date" (both languages) and the input reference page.

### Documentation: input file and per-platform mapping

Two kinds of reference pages under `docs/`, each bilingual (English source, `*.de.md` twin), with diagrams where they help:

- **Input file reference** (for example `docs/input-format.md`): how the library interprets the BatInspector export and what it expects. Cover every field (required or optional, type, unit, example), the Europe/Berlin interpretation of `Date`,
  the reference-recording rule (one entry per species, night and place), species name normalization, evidence rules (absolute paths, `.png` / `.wav`, checked content), the per-entry validation and `Rejected`, and what a rejected entry or a skipped candidate looks like to the host.
  The README keeps a short summary and links here.
- **One page per adapter / output platform** (`docs/inaturalist.md`; naturgucker only after its API is known): what lands where. A table or diagram from each input field to the platform field (`SpeciesLatin` to taxon, `Date` to `observed_on_string` as a date only,
  coordinates to position and place guess, `Comment`, temperature and humidity to the description text, spectrogram to photo, audio to sound), plus the processing flow (taxon resolution, duplicate check, create, attach), the `PublishStatus` outcomes and what is public and irreversible.
  Prefer an SVG or Mermaid diagram that renders on GitHub. Check that `scripts/validate-config.sh` copes with it.
- Keep `docs/inaturalist-setup.md` (registration and OAuth) separate and link the pages to each other.

### iNaturalist upload limits (file size and format)

Evidence files are read completely into memory (one candidate at a time) and uploaded as is. Nothing checks size or format limits yet, so an oversized file only fails at the upload, after the observation was already created
(see "Rethink part-uploads").

- Look up iNaturalist's limits for observation photos and sounds: maximum file size, accepted image and audio formats and sample rates, duration limits. Check the v2 API docs and the sound upload rules; confirm with a real upload in the live test.
- Reject files over the limit in the adapter pre-flight (`Adapters/INaturalist`, not `Core`), before the observation is created, with a clear `SkippedInvalidEvidence` message. It also caps memory use.
- Decide whether a too large spectrogram PNG should be re-encoded or left to the host, and what to do with WAV recordings over the sound limit (skip, or host provides a compressed copy).
- Add the limits to `docs/inaturalist-setup.md`.

### First live test against iNaturalist

The ported code was only tested against stubs. Also confirm that the silent re-exchange of the stored OAuth token for a new API JWT works (no browser the second day). Run a real dry run and then a single real observation (with a test account
or one that may be deleted afterwards) before anything is released. Also verify whether `observed_on_string` with a time
works on v2 (today only the date is sent; `time_observed_at` is rejected by v2).

Checklist for the live test (things stubs cannot prove):

- When the response has only a uuid and no numeric id, `Url` stays null. Check whether `https://www.inaturalist.org/observations/{uuid}` resolves and use it if so.
- The duplicate check sends `mine_only=true` to v1 `/observations`. Confirm the parameter exists and really restricts to the user's own observations; if it is ignored, the check matches other users' observations and reports false duplicates. Compare the result with and without it (and with `user_id` / `user_login` as the alternative).

### Implement NABU|naturgucker integration

Blocked: there is no public API documentation, so the auth and submission model is unknown.

- Identify and contact NABU/naturgucker developers: API (REST?), auth (API key, OAuth, basic, manual file import?), rate limits,
  terms for automated submission, whether this bat-monitoring use case is welcome.
- Then implement `Adapters/Naturgucker` (currently an internal stub) with its own auth, and only then revisit
  `IObservationPublisher` before 1.0. Do not guess its shape in the meantime.

### GitHub repository polish (settings on github.com, after the first push)

Files are in the repo (README badges, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, issue forms, Dependabot config). Still to click:

- About box: description, topics (`bats`, `bioacoustics`, `inaturalist`, `citizen-science`, `dotnet`, `nuget`), social preview image.
- Security: enable private vulnerability reporting (needed by `SECURITY.md`), secret scanning with push protection, Dependabot alerts.
- Ruleset for `main`: require the CI checks, no force pushes. Optional: squash merge only, delete branches after merge.
- After the first NuGet release: add the NuGet version and downloads badges to both READMEs.
- Optional, later: API docs site on GitHub Pages generated with DocFX from the XML docs; `CITATION.cff` if the tool should be citable.

## Later / carried over

- `net8.0` stays for now (decided 2026-10-02). Revisit only if keeping it becomes a burden.
- Versioned serialized result schema: only when BatInspector wants to persist results.
- Decide whether to register the iNaturalist OAuth application as public (no secret) if BatInspector is distributed to others.
- Re-check iNaturalist API terms and etiquette for automated submission.
- Confirm BatInspector's license is compatible with MIT; add NOTICE if a dependency requires it.
- JSON Schema file for the input format and a contract test, if BatInspector produces the file.
- `CONTRIBUTING.md`, issue templates, deprecation policy for schema changes.

## Dropped

- Geoprivacy / sensitive-species handling: iNaturalist obscures sensitive taxa itself, other platforms may not support it at all,
  and the package cannot solve it (decided 2026-10-02).

## Done

- 2026-10-03: Explicit zone in the input file: optional `TimeZone` (IANA id) per entry, additive within schema v1; `InputDocument.Warnings` with the ambiguous-hour warning; tests incl. foreign-zone gap and future check.
- 2026-10-03: Times and time zones (offline part): `ObservedAt` is a `DateTimeOffset` (Europe/Berlin offset); spring-forward gap rejected, repeated hour read as standard time, no fallback without tz data (throws `TimeZoneNotFoundException`);
  future check compares instants; duplicate check and `observed_on_string` use the local day; description shows MEZ/MESZ. Tests for gap, repeated hour, midnight crossing, zone suffix.
- 2026-10-03: Input validation per entry (bad entries go to `InputDocument.Rejected`, the rest is published): absolute evidence paths with `.png` / `.wav` extension, latitude and longitude both 0, dates in the future (against Europe/Berlin time).
  Evidence is read once per candidate, checked (exists, readable, non-empty, PNG / WAV signature) and the checked bytes are uploaded; new status `SkippedInvalidEvidence`.
- 2026-10-02: Repository scaffold, Core, input schema v1 (`SchemaVersion`), iNaturalist adapter, tests, multi-targeting net8.0 + net10.0.
