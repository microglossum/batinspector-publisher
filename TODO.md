# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
It lists work that is still open or blocked, and nothing else: finished work is recorded by the commit and
`CHANGELOG.md`, decisions against something live in `CLAUDE.md`. Read this before starting work and groom it
before every commit (rules in `CLAUDE.md`).

## Open: code and design

### First live test against iNaturalist

Comes first, before `main` is created (see "Create `main`"). The publishing code (create, attach) was only tested against stubs. Run a real dry run and then a single real observation (with a test account
or one that may be deleted afterwards) before anything is released. What stubs cannot prove:

- A response with only a uuid and no numeric id leaves `Url` null. Check in a browser whether `https://www.inaturalist.org/observations/{uuid}` resolves (curl gets 403 from www) and use it if so.
- `GET /v1/users/me` must carry `id` (the duplicate check sends it as `user_id`).
- `GET /v2/taxa/autocomplete` must return `ancestor_ids` with `fields=...,ancestor_ids` (the v2 field parser accepts it and v2 hands autocomplete to v1, per the source). Without it every name is skipped as "not a bat", which fails safe but blocks all publishing.
- Resume: the v1 search must return `tags` as a list of strings holding the `batinspector:` tag sent in `tag_list` (another shape is ignored, so resume would never trigger), and an upload that is not processed yet must already be listed. Also check that iNaturalist keeps the tag as sent (colon, lower case) and that a 25-character tag is accepted. Otherwise the observation is deliberately left alone (`SkippedDuplicate`) and resume never triggers. Check with a real half-created observation (stop after the photo, or delete the sound in the web UI).
- Dry run against an entry that is already published and against a half-created one: it must say `SkippedDuplicate` and `WouldResume`, and a commit run must then agree.
- Group values (`Nyctaloid`, `Mbart`) are filed under a broader taxon (Chiroptera, *Myotis*), and the duplicate check searches that taxon including everything below it. So any own observation of a bat species on the same day within the radius counts as a duplicate of the group entry, and the group entry is skipped (a species entry is not blocked by an earlier group observation, so the result depends on the order of publishing). Check on a real case, then decide whether group entries need a stricter match, for example on `species_guess` (needs the next item first).
- `species_guess` must keep the original value (`Nyctaloid`) when `taxon_id` is Chiroptera; see how iNaturalist shows it.
- Login port fallback: occupy port 45679 and run the SmokeTest `login`. The login then listens on an OS-assigned port and iNaturalist must accept that redirect URI (the source, Doorkeeper 5.6.6, says the port of a loopback IP redirect URI is ignored; not seen live). The options also accept `localhost` and `[::1]` as redirect host; the docs recommend `127.0.0.1`, and the port fallback may not work for `localhost` at iNaturalist.
- Login refusal: click "Deny" on iNaturalist's authorize page and see what the redirect carries. The listener ends the login at once on an `error` parameter without a `state` or with the right one (`Denied`); if iNaturalist sends the error with a different state, the login only ends at the timeout.
- Retry and pacing (defaults `MaxAttempts = 1`, `MinRequestInterval` 1 s): the limits come from the forum and secondary sources (100 requests per minute, 60 asked for, about 10,000 per day); iNaturalist's "API recommended practices" page answers 403 to our tools, so read it in a browser and compare with `docs/inaturalist-setup.md`.
  Check whether a 429 carries `Retry-After` (otherwise the code backs off from `RetryBaseDelay`; a throttle window of a minute may need a larger `RetryBaseDelay` or `MaxAttempts`), whether writes have a stricter limit than reads, and what v2 answers when overloaded. The default stays `MaxAttempts = 1`; use the findings to document a sensible value for hosts that opt in. Also confirm that the enforced 1 s minimum for `MinRequestInterval` (pacing can no longer be switched off) matches the limit.
- Stored OAuth token: per the source it never expires and no refresh token is issued. Confirm that the token stays valid across days (`renew` after a day or more).
- Login as a non-admin Windows user (BatInspector's normal case; CI's Windows runners run as admin, so CI cannot show it). `OAuthFlow` registers `http://127.0.0.1:<port>/` with `HttpListener`, which on Windows goes through http.sys; a prefix without a URL reservation (`netsh http add urlacl`, needs an admin) is refused for a non-admin with `HttpListenerException`, error 5. Research (2026-10-14, secondary sources only): the port number plays no role, the reservation is per prefix; `localhost` is reported to work without admin on Windows 7 / 8.1, where `127.0.0.1` did not; `127.0.0.1` is reported to work on Windows 10 (one remark in googleapis/google-api-dotnet-client#1055, not Microsoft documentation); nothing found about Windows 11. So the old assumption "an IP prefix needs a reservation" may be too pessimistic. Run SmokeTest `login` as a standard user on Windows 11 (and 10 if available) with the fixed port and with a fallback port. Error 5 already has its own message and skips the fallback ports; the check should also confirm that the code is 5 on a real machine (`ErrorCode`) and that the `netsh http add urlacl` hint in the message and in the setup guide works. If a normal user is refused for `127.0.0.1`:
  - replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` that reads one request line and writes a fixed response (avoids http.sys and its URL ACLs, and the verbatim Host-header match); the existing `OAuthFlowTests` talk HTTP to the port and keep working. Not `localhost` as a workaround: it needs a second registered redirect URI and the Host-header match gets awkward.
  - If it works, drop the `netsh` hint's "not verified" wording in `docs/inaturalist-setup.md` / `.de.md` and keep the error-5 message for locked-down machines.
- Time and zone: the package sends `observed_on_string` as `yyyy-MM-dd HH:mm:ss` and no zone, because the source says iNaturalist takes the zone from the coordinates (`set_time_zone` runs before the string is parsed, and a zone in the string is ignored for a georeferenced observation). Check on a real observation that the page shows the sent local time and the zone of the place, also for a night after midnight, and that the server's "not in the future" check (it uses the observation's zone) passes for a recording from the last hours. Optional: send a non-default `TimeZone` entry (for example `Europe/Lisbon` with Portuguese coordinates) and confirm the same.
- How long the API JWT really lives (assumed 24 h), and that an invalidated JWT is answered with 401 (the publisher renews on 401 only; a 403 is not renewed). What a revoked app authorization answers.

### Taxon names for the BatInspector owner

- Hand over `docs/batinspector-taxa-check.de.md`: `Eptesicus nilssonii` is a synonym (iNaturalist lists *Cnephaeus nilssonii*) and `Myotis oxygnatus` is unknown (*M. oxygnathus* is a synonym of *Myotis blythii* there). Both entries are skipped by design; the fix and the decision about *M. blythii* belong to BatInspector's export.
- BatInspector exports the raw filename abbreviation when it has no species entry (for example `TTEN`, Tadarida teniotis, in its regional lists but not its species list). Such entries are skipped by design; the fix belongs into BatInspector's export.

### iNaturalist validation: verify the rules live

`INaturalistValidator` refuses what iNaturalist would reject; its rules (listed in `docs/inaturalist-setup.md`) come from iNaturalist's open source and forum and are **not yet verified live**.

- Upload a real recording and a spectrogram of realistic size, then one deliberately over 20 MB (a test account or an observation that may be deleted): which status and body does v2 return, and is the real limit 20 MB or 20 MiB (`MaxEvidenceBytes` defaults to the safe 20,000,000)? Is a high sample rate (192-500 kHz) accepted, and does the observation page play it and show its spectrogram? (`local/` is off limits for Claude.)
- Test the other rules: latitude exactly 90, a tag list over the limit. Adjust or drop a rule that iNaturalist does not enforce.
- A WAV over the limit has no automatic way out; see whether BatInspector can export a shorter or resampled copy.
- The evidence files are read completely into memory before the size is checked. Fine for one candidate at a time; revisit only if memory becomes a problem.

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

### Create `main` (squash of `tech/setup-repo`)

Decided 2026-10-14: do the first live test (see "First live test against iNaturalist") first, on `tech/setup-repo`, and squash afterwards, so that the fixes it brings are part of the first `main` commit. `main` does not exist yet, so the squash is a root commit:

- Groom this file, then tag the old tip (`git tag archive/setup-repo tech/setup-repo`): the tag keeps the full commit messages, `main` stays clean.
- `git checkout --orphan main`, commit with a carefully written message (the permanent record: the guiding decisions grouped by theme, not the 40 messages strung together; most decisions are already in `CLAUDE.md` and `CHANGELOG.md`).
- `git diff tech/setup-repo main` must be empty. Push `main` and the tag only with the owner's OK, then continue with "GitHub Actions" below (default branch, delete `tech/setup-repo`; Dependabot targets the default branch, so its next PRs follow).
- Check first whether the nuget.org trusted publishing policy or `release.yml` depend on a branch or environment name (`docs/releasing.md`); set the first release tag only on the new `main` commit (MinVer counts the height from there).

### GitHub Actions

- `tech/setup-repo` is currently the default branch and `main` does not exist on the remote, so Dependabot PRs target the working branch (its branches were all cleared 2026-10-14, the bumps are in). Push `main`, make it the default, so that new Dependabot PRs target `main` (steps above). Needed before the first release: the README links, the CI badge and the package's release-notes link all point to `main` and 404 on nuget.org until it exists (seen in the upload preview 2026-10-14);
- after the push: run `release.yml` once by hand (Actions > Release > Run workflow, a dry run: build, test, pack, notes, no push; the "Run workflow" button only appears once the file is on the default branch) and check that it is green. The workflow has never run; its tag path (login, push, GitHub Release, the check that the tagged commit is on `main`) can only be tested by a real tag;
- check that the gitleaks job still works with the workflow-level `permissions: contents: read` on pull requests (added 2026-10-14; the action may need `pull-requests: read`), and that Dependabot keeps the SHA-pinned actions (`# vN` comments) current;
- optional: a musl test leg in an Alpine job container (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`), only if a consumer runs on Alpine;
- remove `.github/actionlint.yaml` once a released actionlint knows the `ubuntu-26.04` label;
- the actionlint version in the `validate-config` job is pinned by hand (Dependabot does not see it): bump it together with the devcontainer feature now and then.

### First NuGet release

Runbook: `docs/releasing.md`. Open before the first tag:

- Prerequisites: the first live test (above), `main` as the default branch and the dry run of `release.yml` (see "GitHub Actions"), a `CHANGELOG.md` section for the version.
- Check that 2FA is enabled on the nuget.org account.
- Preview the package page again once `main` exists (`dotnet pack src/BatInspectorPublisher -c Release -o artifacts`, upload the `.nupkg` on nuget.org "Upload Package", stop before "Submit"): the README links, the CI badge, the icon at the top of `README.md` (raw.githubusercontent.com URL on `main`; relative images do not render on nuget.org) and the release-notes link all point to `main` and 404 until it exists.
- First release as `0.1.0-preview.N`. Versions are immutable: they can be unlisted but not deleted.
- After the first version is out: narrow the trusted publishing policy scope to "Push only new package versions"; if `release.yml` is ever renamed, update the policy (it matches the file name).
- Optional, owner skipped it for now (2026-10-14): reserve the ID prefix `BatInspectorPublisher` (not `BatInspector*`, which would also claim the BatInspector project's IDs) by e-mail to `account@nuget.org`, best after the first release.

### NuGet package quality: open items

- Optional: replace the placeholder icon `assets/icon.png` (128x128, transparent corners) with a better one.
- Later: `PackageValidationBaselineVersion` once a stable version exists (it compares against a stable release, so a `0.x` preview has none); `Microsoft.CodeAnalysis.PublicApiAnalyzers` only if the `ArchitectureTests` allowlist stops being enough (it already fails on an unplanned public type, but does not track members).
- Not researched yet: trimming and AOT annotations, SBOM, a DocFX site.

### GitHub repository polish (settings on github.com, after the first push)

- About box: description, topics (`bats`, `bioacoustics`, `inaturalist`, `citizen-science`, `dotnet`, `nuget`), social preview image (GitHub wants 1280x640 px; compose it from `assets/icon.png`).
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
- Re-check iNaturalist API terms (not the rate limits, see the live test) for automated submission.
- Logging: `ActivitySource` and metrics (OpenTelemetry), if a host wants them.
- BatInspector's licence is unclear (checked 2026-10-14): the `LICENSE` file on its `master` branch is CC BY-NC-SA 4.0, `BatInspector/Licenses.txt` says CC BY-NC 4.0, and GitHub detects none. Ask the owner which applies, then replace the "unclear" note in `CLAUDE.md` with the answer. This package contains none of its code, reads only the file format and links to it, so MIT stays possible; keep it that way (a copy of its code would bring the NC / SA terms along). NOTICE: no dependency requires one so far.
- Deprecation policy for schema changes.
