# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
It lists work that is still open or blocked, and nothing else: finished work is recorded by the commit and
`CHANGELOG.md`, decisions against something live in `CLAUDE.md`. Read this before starting work and groom it
before every commit (rules in `CLAUDE.md`).

## Open: code and design

### First live test against iNaturalist

The publishing code (create, attach) was only tested against stubs. Run a real dry run and then a single real observation (with a test account
or one that may be deleted afterwards) before anything is released. What stubs cannot prove:

- A response with only a uuid and no numeric id leaves `Url` null. Check in a browser whether `https://www.inaturalist.org/observations/{uuid}` resolves (curl gets 403 from www) and use it if so.
- `GET /v1/users/me` must carry `id` (the duplicate check sends it as `user_id`).
- `GET /v2/taxa/autocomplete` must return `ancestor_ids` with `fields=...,ancestor_ids` (the v2 field parser accepts it and v2 hands autocomplete to v1, per the source). Without it every name is skipped as "not a bat", which fails safe but blocks all publishing.
- Resume: the v1 search must return `tags` as a list of strings holding the `batinspector:` tag sent in `tag_list` (another shape is ignored, so resume would never trigger), and an upload that is not processed yet must already be listed. Also check that iNaturalist keeps the tag as sent (colon, lower case) and that a 25-character tag is accepted. Otherwise the observation is deliberately left alone (`SkippedDuplicate`) and resume never triggers. Check with a real half-created observation (stop after the photo, or delete the sound in the web UI).
- Dry run against an entry that is already published and against a half-created one: it must say `SkippedDuplicate` and `WouldResume`, and a commit run must then agree.
- Group values (`Nyctaloid`, `Mbart`) are filed under a broader taxon, and the duplicate check treats any own species-level observation of that day and place as a duplicate. Decide whether they need a stricter match (for example by `species_guess`).
- `species_guess` must keep the original value (`Nyctaloid`) when `taxon_id` is Chiroptera; see how iNaturalist shows it.
- Login port fallback: occupy port 45679 and run the SmokeTest `login`. The login then listens on an OS-assigned port and iNaturalist must accept that redirect URI (the source, Doorkeeper 5.6.6, says the port of a loopback IP redirect URI is ignored; not seen live).
- Login refusal: click "Deny" on iNaturalist's authorize page and see what the redirect carries. The listener ends the login at once on an `error` parameter without a `state` or with the right one (`Denied`); if iNaturalist sends the error with a different state, the login only ends at the timeout.
- Retry and pacing (defaults `MaxAttempts = 1`, `MinRequestInterval` 1 s): the limits come from the forum and secondary sources (100 requests per minute, 60 asked for, about 10,000 per day); iNaturalist's "API recommended practices" page answers 403 to our tools, so read it in a browser and compare with `docs/inaturalist-setup.md`.
  Check whether a 429 carries `Retry-After` (otherwise the code backs off from `RetryBaseDelay`; a throttle window of a minute may need a larger `RetryBaseDelay` or `MaxAttempts`), whether writes have a stricter limit than reads, and what v2 answers when overloaded. Then decide whether `MaxAttempts = 3` should become the default.
- Stored OAuth token: per the source it never expires and no refresh token is issued. Confirm that the token stays valid across days (`renew` after a day or more).
- Login as a non-admin Windows user (BatInspector's normal case; CI's Windows runners run as admin, so CI cannot show it). `OAuthFlow` registers `http://127.0.0.1:<port>/` with `HttpListener`, which on Windows goes through http.sys; an IP prefix usually needs a URL reservation (`netsh http add urlacl`) for non-admins and then fails with "access denied" (`HttpListenerException`, error 5). The code reads that as "port in use", tries the fallback ports and reports `ListenerUnavailable`. If confirmed, see "Login listener on Windows" below.
- How long the API JWT really lives (assumed 24 h), and that an invalidated JWT is answered with 401 (the publisher renews on 401 only; a 403 is not renewed). What a revoked app authorization answers.

### Taxon names for the BatInspector owner

- Hand over `docs/batinspector-taxa-check.de.md`: `Eptesicus nilssonii` is a synonym (iNaturalist lists *Cnephaeus nilssonii*) and `Myotis oxygnatus` is unknown (*M. oxygnathus* is a synonym of *Myotis blythii* there). Both entries are skipped by design; the fix and the decision about *M. blythii* belong to BatInspector's export.
- BatInspector exports the raw filename abbreviation when it has no species entry (for example `TTEN`, Tadarida teniotis, in its regional lists but not its species list). Such entries are skipped by design; the fix belongs into BatInspector's export.

### iNaturalist validation: verify the rules live

`INaturalistValidator` refuses what iNaturalist would reject; its rules (listed in `docs/inaturalist-setup.md`) come from iNaturalist's open source and forum and are **not yet verified live**.

- Upload a real recording and a spectrogram of realistic size, then one deliberately over 20 MB (a test account or an observation that may be deleted): which status and body does v2 return, and is the real limit 20 MB or 20 MiB (`MaxEvidenceBytes` defaults to the safe 20,000,000)? Is a high sample rate (192-500 kHz) accepted, and does the observation page play it and show its spectrogram? (`local/` is off limits for Claude.)
- Test the other rules: latitude exactly 90, a tag list over the limit. Adjust or drop a rule that iNaturalist does not enforce.
- The server also refuses a date after today in the account's own time zone, which the validator cannot know (only reachable for a recording whose zone is ahead of the account's, since a future instant is rejected); sending the zone (see "Times and time zones") would let the server decide.
- A WAV over the limit has no automatic way out; see whether BatInspector can export a shorter or resampled copy.
- The evidence files are read completely into memory before the size is checked. Fine for one candidate at a time; revisit only if memory becomes a problem.

### Times and time zones

Needs the live test: send the time as well, with the zone (the candidate carries `TimeZoneId`), so the platform does not read it in the account's zone. Find out whether v2 accepts a time in `observed_on_string`, which zone parameter it expects (and in which name format),
and whether `time_observed_at` is really rejected. Then update every page that says "iNaturalist receives only the calendar date" (`docs/inaturalist.md` field table and steps, `docs/input-format.md`, and the README if it still says it; both languages).

### Input validation

- The signature checks are not a full format validation (a file can start like a PNG and still be something else); decide whether that is enough.

### Hardening before the first release

- **`INaturalistToken.ToString()` prints both tokens.** It is a public `record`, so the generated `ToString()` contains the JWT and the never-expiring OAuth token; a host that logs or displays the object leaks the login. Override `PrintMembers` to show only `Username` and the expiry, and add a test that neither token appears.
- **Login listener on Windows** (only if the live check above confirms the problem): replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` that reads one request line and writes a fixed response (avoids http.sys and its URL ACLs, and the verbatim Host-header match). The existing `OAuthFlowTests` talk HTTP to the port and keep working. Independently of the check: map `HttpListenerException` error 5 to its own message instead of trying fallback ports.
- **Upload timeout option.** `HttpClient.Timeout` (default 100 s) also limits the evidence uploads, and a timed-out upload is not repeated (it is a write), so on a slow line resume times out again in the same way. The setup guide tells hosts to pass a client with a larger timeout. Decide whether an `UploadTimeout` option (a linked `CancellationTokenSource` in the upload, kept apart from caller cancellation) is worth it on top.
- **`ConfigureAwait(false)` on every `await` in `src/`.** The host is a Windows desktop app: without it every continuation returns to the UI thread (JSON parsing and upload preparation run there), and a host that blocks on `.Result`/`.Wait()` from the UI thread deadlocks. Enforce it with `CA2007` as a warning for the library project only (not the tests), so CI keeps it. `IProgress<T>` still reaches the UI through `Progress<T>`.

### Public API before 1.0

Each of these is trivial now and a breaking change after 1.0:

- `InputSchemaReader.CurrentSchemaVersion` is a public `const`, which is inlined into the host at compile time (a host that is not recompiled keeps the old value): make it a static property.
- `InputSchemaReader.Read(Stream)` disposes the caller's stream (`StreamReader` without `leaveOpen`): leave it open, or document it.
- Decide whether an async `InputSchemaReader.ReadFileAsync` belongs in the API (the rest is async; the file is small).

### Tests

- **The login tests must not be able to hang.** `TestData.Options` keeps the default `AuthorizationTimeout` of 5 minutes, and the fake browsers in `INaturalistAuthenticatorTests` (three copies) and `LoggingTests` call the configured redirect URI fire-and-forget. If the port is taken again before the listener binds (parallel tests), `OAuthFlow` correctly falls back to another port, the fake browser still calls the old one, and the test waits out the 5 minutes. Use one shared fake browser that reads `redirect_uri` from the authorize URL (`OAuthFlowTests.Browser` already does), set a 10 s timeout in the test options, and await the browser task at the end of each test.
- **Make `ArchitectureTests` complete.** NetArchTest passes when no type matches, so a renamed namespace turns a rule green forever: assert that each selection is non-empty. Derive the adapter pairs from the namespaces under `BatInspectorPublisher.Adapters.` instead of hard-coding them. Add rules for: an allowlist of the public types (26 today), so an accidental `public` on an API client or wire model fails; no dependency on `System.Console` in the library; `Adapters.Naturgucker` not public.

### Test the Windows-only code (DPAPI) without a Windows machine

`ProtectedFileTokenStore` uses DPAPI on Windows; the `[WindowsOnlyFact]` test (`Save_OnWindows_EncryptsTheFile`) is skipped on Linux. The CI matrix has a Windows leg, so every push is covered; what is missing is a local check before pushing. Options:

- First step: abstract DPAPI behind a small internal seam (`IDataProtector`-like) so the file logic (atomic write, corrupt file as "no token", fallbacks) is tested on Linux with a fake protector, and only the 5-line DPAPI call stays Windows-only.
- Wine in the devcontainer, only if a real gap remains after the seam: does Wine's `crypt32` DPAPI behave like Windows (per-user key, `Unprotect` failing for foreign data)? Is a Windows .NET runtime usable under Wine, and is it worth its weight in the image?
- A Windows VM (a Windows container is no option for the Linux devcontainer).
- Cover the same way if wanted: the `Process.Start` browser launch in the SmokeTest, and the Europe/Berlin time zone ID differences (`W. Europe Standard Time` vs IANA) if tz data ever differs on Windows.

A CI-only check is acceptable if the seam leaves no gap.

### Cross-platform token storage

Waits until BatInspector runs off Windows:

- Real secret storage per OS (macOS Keychain, Linux Secret Service / libsecret, kwallet): left to the host through `INaturalistTokenStore`; a Keychain / Secret Service package is deferred.
- Do not design a shared "secure value store" abstraction from one adapter. Revisit once a second adapter (naturgucker) shows what it needs to store.

## Open: release and repository

### GitHub Actions

- `tech/setup-repo` is currently the default branch and `main` does not exist on the remote, so Dependabot PRs target the working branch. Push `main`, make it the default, let Dependabot retarget;
- release workflow per `docs/releasing.md`: checkout with `fetch-depth: 0` (MinVer reads the tag), pack, push to nuget.org, create the GitHub Release with notes extracted from `CHANGELOG.md`;
- harden the workflow: pin actions to commit SHAs (Dependabot keeps them current) and set `permissions: contents: read` at the top (check what gitleaks needs on pull requests);
- optional: a musl test leg in an Alpine job container (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`), only if a consumer runs on Alpine;
- remove `.github/actionlint.yaml` once a released actionlint knows the `ubuntu-26.04` label;
- the actionlint version in the `validate-config` job is pinned by hand (Dependabot does not see it): bump it together with the devcontainer feature now and then.

### First NuGet release

The package ID `BatInspectorPublisher` was free on nuget.org on 2026-10-02. Steps:

1. Create a nuget.org account (sign in with a Microsoft account) and enable 2FA.
2. Optional: reserve the ID prefix `BatInspector*` (free request, gives the verified badge).
3. Create an API key with Push scope, restricted to the glob `BatInspectorPublisher*`, with an expiry date.
   Alternative: nuget.org trusted publishing via GitHub OIDC, which avoids a long-lived key (verify current availability).
4. Store the key as the GitHub secret `NUGET_API_KEY`.
5. Tag-triggered release workflow, see `docs/releasing.md`.
6. First release as `0.1.0-preview.N`. Versions are immutable: they can be unlisted but not deleted.

Prerequisites: the first live test, CHANGELOG, README check.

### Research: how to build really good NuGet packages

Before the first release, research current best practice for a high-quality package, then decide what to adopt:

- Microsoft's .NET library guidance (learn.microsoft.com/dotnet/standard/library-guidance) and NuGet's package authoring best practices.
- Metadata and discoverability: README rendering on nuget.org, package icon, release notes link.
- API quality: public API tracking (`Microsoft.CodeAnalysis.PublicApiAnalyzers`), package validation with a baseline version (`EnablePackageValidation`), nullable annotations, XML docs coverage.
- Compatibility: multi-targeting choices, minimum dependency versions, trimming and AOT annotations, minimal dependencies.
- Supply chain: package signing, trusted publishing (OIDC) instead of long-lived API keys, NuGet audit, SBOM.
- Documentation: a docs site (DocFX on GitHub Pages) versus README only, samples, how a consumer discovers the BatInspector integration.
- Release hygiene: pre-release flow (`-preview.N`, `-rc.N`), deprecation and unlisting policy, prefix reservation.
- Look at well-regarded small packages and copy what works.

Output: a short decision list in this file (adopt, later, skip) and the resulting items in CI and the csproj.

### GitHub repository polish (settings on github.com, after the first push)

- About box: description, topics (`bats`, `bioacoustics`, `inaturalist`, `citizen-science`, `dotnet`, `nuget`), social preview image.
- Security: enable private vulnerability reporting (needed by `SECURITY.md`), secret scanning with push protection, Dependabot alerts.
- Ruleset for `main`: require the CI checks, no force pushes. Optional: squash merge only, delete branches after merge.
- After the first NuGet release: add the NuGet version and downloads badges to both READMEs.
- Optional, later: API docs site on GitHub Pages generated with DocFX from the XML docs; `CITATION.cff` if the tool should be citable.

## Blocked

### Implement NABU|naturgucker integration

Blocked: there is no public API documentation, so the auth and submission model is unknown.

- Identify and contact NABU/naturgucker developers: API (REST?), auth (API key, OAuth, basic, manual file import?), rate limits,
  terms for automated submission, whether this bat-monitoring use case is welcome.
- Write `docs/naturgucker.md` (bilingual, like `docs/inaturalist.md`) together with the adapter.
- Then implement `Adapters/Naturgucker` (currently an internal stub) with its own auth, and only then revisit
  `IObservationPublisher` before 1.0. Do not guess its shape in the meantime.
  The public types to revisit then are the iNaturalist-shaped parts of the result model: `PublishStep` (iNaturalist's
  order: create, then attach the photo, then the sound), `PublishResult.SpectrogramAttached` / `AudioAttached` (on a
  platform without sound upload every `Created` would look incomplete) and `WouldResume` / `Resumed` (exist only because
  iNaturalist's submission takes several requests). Do not redesign them before then: with one platform any redesign is
  a guess.

## Later / carried over

- Revisit `net8.0` only if keeping it becomes a burden.
- Versioned serialized result schema: only when BatInspector wants to persist results.
- Decide whether to register the iNaturalist OAuth application as public (no secret) if BatInspector is distributed to others.
- Re-check iNaturalist API terms (not the rate limits, see the live test) for automated submission.
- Logging: `ActivitySource` and metrics (OpenTelemetry), if a host wants them.
- Confirm BatInspector's license is compatible with MIT; add NOTICE if a dependency requires it.
- JSON Schema file for the input format and a contract test, if BatInspector produces the file.
- Deprecation policy for schema changes.
- Merge the two `GET /v1/users/me` calls in `INaturalistApiClient` (username and user id) into one parse.
