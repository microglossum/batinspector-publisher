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
- Do not design a shared "secure value store" abstraction from one adapter. Revisit once a second adapter
  (naturgucker) shows what it needs to store.
- The host's own OAuth client ID/secret settings need a cross-OS home too, but that is the host's concern.
- Needs a Linux/macOS CI leg that exercises the new store.

### Rethink taxon matching

Today: exact case-insensitive name match on `/v2/taxa/autocomplete`; no match means `SkippedUnresolvedTaxon`; there is no
fallback to the first hit (that would file observations under wrong species). Open questions:

- iNaturalist taxonomy versus German/EU bat checklists: synonyms, splits and lumps (Myotis mystacinus/brandtii,
  Plecotus, ...). Does autocomplete return inactive or synonym taxa?
- Genus-level and uncertain calls (BatInspector codes like "Nyctaloid" or "?"): how will they appear in `SpeciesLatin`,
  and what should happen? Rank checks?
- Several exact matches, a host-supplied name override map, caching misses, regression fixtures from real responses.
Decide the expected behavior with real BatInspector data before changing code.

### Rethink part-uploads

Today: create observation, then attach spectrogram, then audio. If an attachment fails the result is `Failed` with
`ObservationId` and the attached flags set. Nothing retries or cleans up, and the next run's duplicate check finds the
half-created observation and reports `SkippedDuplicate`, so its evidence is never completed. Options to evaluate:

- resume: attach missing evidence to an existing observation id; make the duplicate check aware of missing evidence;
- roll back: delete the observation if evidence cannot be attached (check what the API allows);
- retry with backoff for transient errors, and rate limiting against iNaturalist's API etiquette;
- upload order, and what `PublishResult` should say about partial states (ties into a versioned result schema).

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
- Document the environments where the loopback login does not work and what to do.

### Input validation criteria

Today the reader checks: valid JSON, `SchemaVersion`, required fields and types, the date format, coordinate ranges, non-empty strings; the orchestrator checks that evidence files exist.
All-or-nothing is the decided behavior (one bad entry rejects the file). Principles:

- Validate once at the boundary, report every problem with its path, never alter data silently (except documented normalization such as species capitalization).
- Two levels: **error** (the whole file is rejected: decided 2026-10-02, all-or-nothing, single bad entries are not skipped) and **warning** (reported, does not block).
- Publishing is public and irreversible, so anything doubtful that cannot be fixed afterwards is an error, not a warning.
- Platform limits belong in the adapter's pre-flight, not in `Core`.

Candidate checks:

- Errors: latitude and longitude both exactly 0 (missing GPS); date in the future; evidence paths with the wrong extension (`.png` / `.wav`); evidence files that are empty or lack the PNG / WAV signature bytes; unreadable files.
- Warnings: implausible temperature or humidity (omit the value from the description); a daytime timestamp for a bat; duplicate entries (same species, time and place; the remote duplicate check can lag); a name that is neither a binomial nor a genus.
- iNaturalist pre-flight: file size and format limits (look up), date not in the future.
- API: keep the strict `Parse` / `Read` / `ReadFile` that throw, and keep them working for inline JSON strings and streams, not only files. If warnings are added, expose them through a report-style result next to the candidates; errors still throw.

### First live test against iNaturalist

The ported code was only tested against stubs. Also confirm that the silent re-exchange of the stored OAuth token for a new API JWT works (no browser the second day). Run a real dry run and then a single real observation (with a test account
or one that may be deleted afterwards) before anything is released. Also verify whether `observed_on_string` with a time
works on v2 (today only the date is sent; `time_observed_at` is rejected by v2).

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
- Versioned serialized result schema (plan section 2.3): only when BatInspector wants to persist results.
- Decide whether to register the iNaturalist OAuth application as public (no secret) if BatInspector is distributed to others.
- Re-check iNaturalist API terms and etiquette for automated submission.
- Confirm BatInspector's license is compatible with MIT; add NOTICE if a dependency requires it.
- JSON Schema file for the input format and a contract test, if BatInspector produces the file.
- `CONTRIBUTING.md`, issue templates, deprecation policy for schema changes.

## Dropped

- Geoprivacy / sensitive-species handling: iNaturalist obscures sensitive taxa itself, other platforms may not support it at all,
  and the package cannot solve it (decided 2026-10-02).

## Done

- 2026-10-02: Repository scaffold, Core, input schema v1 (`SchemaVersion`), iNaturalist adapter, tests, multi-targeting net8.0 + net10.0.
