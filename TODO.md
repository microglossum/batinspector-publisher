# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
It lists work that is still open or blocked, and nothing else: finished work is recorded by the commit and
`CHANGELOG.md`, decisions against something live in `CLAUDE.md`. Read this before starting work and groom it
before every commit (rules in `CLAUDE.md`).

## Open: code and design

### First live test against iNaturalist

The publishing code (taxa, duplicate check, create, attach) was only tested against stubs; login, stored token and silent JWT renewal already work live (SmokeTest `login`, `whoami`, `renew`). Run a real dry run and then a single real observation (with a test account
or one that may be deleted afterwards) before anything is released. Time handling is tracked under "Times and time zones".

Checklist for the live test (things stubs cannot prove):

- When the response has only a uuid and no numeric id, `Url` stays null. Check whether `https://www.inaturalist.org/observations/{uuid}` resolves and use it if so.
- Resume (new, written from memory of the v1 API, never seen live): the v1 `/observations` search results must carry `description`, `photos` and `sounds`, and `description` must come back as sent (the match ignores whitespace differences only).
  If a field is missing the observation is deliberately left alone (`SkippedDuplicate`), so a wrong assumption fails safe but resume never triggers. Check with a real half-created observation (stop after the photo, or delete the sound in the web UI).
  Also check that an observation with a not yet processed upload already lists its photo or sound.
- Dry run: run it against an entry that is already published and against a half-created one, and check that it says `SkippedDuplicate` and `WouldResume` and that a commit run then agrees.
- The duplicate check sends `mine_only=true` to v1 `/observations`. Confirm the parameter exists and really restricts to the user's own observations; if it is ignored, the check matches other users' observations and reports false duplicates. Compare the result with and without it (and with `user_id` / `user_login` as the alternative).

### Taxon matching: live verification

Needs the live API or BatInspector:

- Live check of the autocomplete response: `rank`, `is_active` and `matched_term` must be present with `fields=id,name,rank,is_active,matched_term` (a missing rank is treated as a mismatch, so a wrong assumption fails safe but skips everything). Check that a synonym search returns the current taxon with `matched_term`, and that inactive taxa are not returned by default.
- Homonyms: an exact genus name that also exists in another kingdom would be skipped as ambiguous. Check `Myotis`, `Plecotus`, `Pipistrellus`, `Eptesicus`, `Vespertilio`, `Chiroptera` live; if one is ambiguous, narrow the search to bats (for example by ancestor) instead of picking one.
- Check that `Myotis oxygnatus` (BatInspector's spelling; the usual spelling is *oxygnathus*) and the other Latin names BatInspector exports (`BatInfo.cs` species list in the BatInspector repository) resolve; list the ones that do not.
- BatInspector exports the raw filename abbreviation when it has no species entry (for example `TTEN`, Tadarida teniotis, which is in its regional lists but not its species list): such entries are skipped by design. The fix belongs into BatInspector's export, not here.
- Check that `species_guess` keeps the original value (`Nyctaloid`) when `taxon_id` is Chiroptera, and how iNaturalist shows it.

### Retry and rate limiting

A transient error (5xx, 429, network) ends the candidate as `Failed` at once; the next run completes it through resume. Open: retry with backoff for transient errors, and rate limiting against iNaturalist's API etiquette (check the documented request limits, and whether `Retry-After` is sent).

### iNaturalist validation: verify the rules live

`INaturalistValidator` (before login and request, also in a dry run) refuses what iNaturalist would reject; the rules are listed in `docs/inaturalist-setup.md`. They come from iNaturalist's open source and forum (read 2026-10-04), **not yet verified live**. Error texts from iNaturalist are passed through unchanged (decided, see `CLAUDE.md`).
Per observation we send one photo and one sound, so the per-observation file count (about 20 photos) is no issue. Facts that the validator relies on or that stay unknown:

- **Size:** 20 MB per file (forum, moderator statements; MB or MiB unknown, `MaxEvidenceBytes` defaults to the safe 20,000,000). The v2 API sets no limit itself and proxies to the Rails app, so the limit is probably enforced in front of it; a forum thread mentions "413 Request Entity Too Large". What the v2 API returns is unknown.
- **Formats:** sounds WAV, MP3, M4A, AAC, AMR (content-detected; WAV and MP3 stored unchanged); photos JPEG, PNG, GIF, HEIC/HEIF, downscaled to 2048 px on the longest edge. No duration or sample-rate limit is documented anywhere.
- **Description:** a `text` column without a known length limit, so it is not validated.
- **Date:** the server also refuses a date after today in the account's own time zone, which the validator cannot know; it only catches dates no zone allows. Sending the zone (see "Times and time zones") would let the server decide correctly.

To do:

- Live test: upload a real recording and a spectrogram of realistic size; then one deliberately over 20 MB (a test account or an observation that may be deleted) to see the status and body the v2 API returns, and whether the real limit is 20 MB or 20 MiB. Is a high sample rate (192-500 kHz) accepted, and does the observation page play it and show its spectrogram? Compare real WAV sizes with the limit (`local/` is off limits for Claude).
- Live test of the other rules: a date one day ahead, latitude exactly 90, a tag list over the limit. Adjust or drop a rule that iNaturalist does not enforce.
- The evidence files are read completely into memory before the size is checked (the loader runs first and knows no platform limit). Fine for one candidate at a time; revisit only if memory becomes a problem.
- A WAV over the limit has no automatic way out (the host has to supply a smaller file); see whether BatInspector can export a shorter or resampled copy.

### Times and time zones

Needs the live test:

- **iNaturalist:** send the time as well, with the zone (the candidate now carries `TimeZoneId`), so the platform does not read it in the account's zone. Find out in the live test whether v2 accepts a time in `observed_on_string`, which zone parameter it expects (and in which name format),
  and whether `time_observed_at` is really rejected. Then update the README sentence "iNaturalist receives only the calendar date" (both languages) and the input reference page.

### Input validation

Principles for any new check: validate at the boundary, report every problem with its path, never alter data silently (except documented normalization such as species capitalization).
Two levels: **error** (the entry is rejected) and **warning** (reported in `InputDocument.Warnings`, does not block). Publishing is public and irreversible, so anything doubtful that cannot be fixed afterwards is an error, not a warning.
Platform limits belong in the adapter's own validation (`INaturalistValidator`), not in `Core`.

- The signature checks are not a full format validation (a file can start like a PNG and still be something else); decide whether that is enough.

### OAuth login robustness (loopback listener)

The login needs a local HTTP listener on `127.0.0.1` (RFC 8252 loopback redirect), which can fail where the host cannot open a port: port already in use,
two sessions on one machine (terminal server), locked-down machines, a browser on another machine than the app (remote/VDI/SSH), sandboxed or packaged apps with loopback isolation.
Facts found in iNaturalist's open source (Doorkeeper 5.6.6 on `main`, checked 2026-10-02; **not yet verified live**):

- OAuth access tokens never expire and no refresh tokens are issued, so the login is needed once.
- For loopback IP redirect URIs (`127.0.0.1`, `::1`, not `localhost`) the **port is ignored** when matching, and the registered redirect URI field accepts several URIs (one per line).
  So an OS-assigned port should work with the one registered URI. Other apps use custom schemes (`myapp://callback`), so those are accepted too.

To do:

- Live check of the port fallback (fixed port first, then up to three OS-assigned ports): occupy port 45679 and run the SmokeTest login. The owner confirmed iNaturalist accepts a changed loopback port.
- A host-pluggable receiver for the authorization response, next to `AuthorizationPrompt`: a manual "paste the redirected URL" fallback for blocked or remote environments, and a custom-scheme receiver for packaged desktop apps.
- Distinct, actionable error types: port in use, timeout, denied by user, token rejected.
- `OAuthFlow.WaitForAuthorizationCodeAsync` accepts only the first request on the listener. Any stray request (port scan, browser prefetch, another local program) ends the login with an error. Keep listening until a request carries a valid `state` or the timeout hits.
- Document the environments where the loopback login does not work and what to do.

### Documentation: input file and per-platform mapping

Two kinds of reference pages under `docs/`, each bilingual (English source, `*.de.md` twin), with diagrams where they help:

- **Input file reference** (for example `docs/input-format.md`): how the library interprets the BatInspector export and what it expects. Cover every field (required or optional, type, unit, example), the Europe/Berlin interpretation of `Date`,
  the reference-recording rule (one entry per species, night and place), species name normalization, evidence rules (absolute paths, `.png` / `.wav`, checked content), the per-entry validation and `Rejected`, and what a rejected entry or a skipped candidate looks like to the host.
  The README keeps a short summary and links here.
- **One page per adapter / output platform** (`docs/inaturalist.md`; naturgucker only after its API is known): what lands where. A table or diagram from each input field to the platform field (`SpeciesLatin` to taxon, `Date` to `observed_on_string` as a date only,
  coordinates to position and place guess, `Comment`, temperature and humidity to the description text, spectrogram to photo, audio to sound), plus the processing flow (taxon resolution, duplicate check, create, attach), the `PublishStatus` outcomes and what is public and irreversible.
  Prefer an SVG or Mermaid diagram that renders on GitHub. Check that `scripts/validate-config.sh` copes with it.
- Keep `docs/inaturalist-setup.md` (registration and OAuth) separate and link the pages to each other.

### Test the Windows-only code (DPAPI) without a Windows machine

`ProtectedFileTokenStore` uses DPAPI on Windows; the `[WindowsOnlyFact]` test (`Save_OnWindows_EncryptsTheFile`) is skipped on Linux, so the devcontainer never runs it. The CI matrix already has a Windows leg, so every push is covered; what is missing is a local way to check it before pushing. Options to evaluate:

- Wine in the devcontainer, running the Windows build of the test assembly (`dotnet test` with a win-x64 runtime under Wine). Open questions: does Wine's `crypt32` DPAPI (`CryptProtectData`) behave like Windows (per-user key, `Unprotect` failing for foreign data, which feeds the corrupt-file path)? Is a Windows .NET SDK/runtime usable under Wine at all, and is the setup worth its weight in the image?
- A Windows container (needs a Windows host with Docker in Windows-container mode, so not an option for the Linux devcontainer) or a Windows VM.
- Abstract DPAPI behind a small internal seam (`IDataProtector`-like) so the file logic (atomic write, corrupt file as "no token", fallbacks) is tested on Linux with a fake protector, and only the 5-line DPAPI call stays Windows-only. This is the cheapest and probably the best first step, independent of Wine.
- Other Windows-only behavior to cover the same way: the `Process.Start` browser launch in the SmokeTest, and the Europe/Berlin time zone ID differences (`W. Europe Standard Time` vs IANA) if tz data ever differs on Windows.

Decide after trying the seam; use Wine only if a real gap remains. A CI-only check is acceptable if it does not.

### Cross-platform token storage

Waits until BatInspector runs off Windows:

- Real secret storage per OS (macOS Keychain, Linux Secret Service / libsecret, kwallet): left to the host through `INaturalistTokenStore`; a Keychain / Secret Service package is deferred.
- The stored OAuth token never expires, so it is a long-lived credential for the user's iNaturalist account (unlike the client secret, which a distributed app cannot keep confidential).
- Do not design a shared "secure value store" abstraction from one adapter. Revisit once a second adapter (naturgucker) shows what it needs to store.
- The host's own OAuth client ID/secret settings need a cross-OS home too, but that is the host's concern.

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
Prerequisites: the first live test (see above), CHANGELOG, README check.

### Research: how to build really good NuGet packages

Before the first release, research current best practice for publishing a high-quality package, then decide what to adopt. Starting points and topics:

- Microsoft's guidance: the .NET library guidance (learn.microsoft.com/dotnet/standard/library-guidance) and NuGet's package authoring best practices.
- Metadata and discoverability: README rendering on nuget.org, package icon, release notes link.
- API quality: public API tracking (`Microsoft.CodeAnalysis.PublicApiAnalyzers`), package validation with a baseline version (`EnablePackageValidation`) to catch breaking changes automatically, nullable annotations, XML docs coverage.
- Compatibility: multi-targeting choices, minimum dependency versions, trimming and AOT annotations, minimal dependencies.
- Supply chain: package signing, nuget.org trusted publishing (OIDC) instead of long-lived API keys, NuGet audit, SBOM.
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
- Then implement `Adapters/Naturgucker` (currently an internal stub) with its own auth, and only then revisit
  `IObservationPublisher` before 1.0. Do not guess its shape in the meantime.

## Later / carried over

- `net8.0` stays for now (decided 2026-10-02). Revisit only if keeping it becomes a burden.
- Versioned serialized result schema: only when BatInspector wants to persist results.
- Decide whether to register the iNaturalist OAuth application as public (no secret) if BatInspector is distributed to others.
- Re-check iNaturalist API terms and etiquette for automated submission.
- Decide whether `INaturalistApiException.Message` (response body included, so also `PublishResult.Message` and the Error log line) gets a length cap: a 5xx can answer with a whole HTML page. The full body stays in `ResponseBody` either way.
- Logging: `ActivitySource` and metrics (OpenTelemetry), if a host wants them.
- Confirm BatInspector's license is compatible with MIT; add NOTICE if a dependency requires it.
- JSON Schema file for the input format and a contract test, if BatInspector produces the file.
- Deprecation policy for schema changes.
