# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
It lists work that is still open or blocked, and nothing else: finished work is recorded by the commit and
`CHANGELOG.md`, decisions against something live in `CLAUDE.md`. Read this before starting work and groom it
before every commit (rules in `CLAUDE.md`).

## Open: code and design

### Remaining live checks against iNaturalist

The publishing flow was tested live on 2026-10-15 with `tools/LiveTest` (see its README). What that run could not show, or left open:

- Group values (`Nyctaloid`, `Mbart`): open question for BatInspector's engineers and specialists (asked 2026-10-15): can the export hold several entries with a group value for one night and place, the same value twice (two `Nyctaloid`) or different ones (`Nyctaloid`, `Social`, `?`)? All group values of a kind share the taxon (Chiroptera), so only the first one published survives. Matching also on `species_guess` would separate `Nyctaloid` from `Social`, but not two `Nyctaloid`. If several are wanted, the rule "one reference recording per species, night and place" needs an explicit exception for group values. Answer first, then decide.
- Login with `localhost` or `[::1]` as redirect host: the options accept both, the docs recommend `127.0.0.1` (the only form tested live); untested at iNaturalist.
- Retry and pacing (defaults `MaxAttempts = 1`, `MinRequestInterval` 1 s): the limits come from the forum and secondary sources (100 requests per minute, 60 asked for, about 10,000 per day); iNaturalist's "API recommended practices" page answers 403 to our tools, so read it in a browser and compare with `docs/inaturalist-setup.md`.
  Check whether a 429 carries `Retry-After` (otherwise the code backs off from `RetryBaseDelay`; a throttle window of a minute may need a larger `RetryBaseDelay` or `MaxAttempts`), whether writes have a stricter limit than reads, and what v2 answers when overloaded. The default stays `MaxAttempts = 1`; use the findings to document a sensible value for hosts that opt in. Also confirm that the enforced 1 s minimum for `MinRequestInterval` (pacing can no longer be switched off) matches the limit.
  So far no rate-limit related response header has been seen in any response, and the pacing holds (about 1 s between request starts). A 429, `Retry-After` and the write limits are unobserved; do not provoke them.
- Stored OAuth token: per the source it never expires and no refresh token is issued. Confirm that the stored login stays valid across days: a dry run of `tools/LiveTest/scenarios/inaturalist/smoke.json` a day or more after 2026-10-15, before the first tag.
- Login as a non-admin Windows user (BatInspector's normal case; CI's Windows runners run as admin, so CI cannot show it). `OAuthFlow` registers `http://127.0.0.1:<port>/` with `HttpListener`, which on Windows goes through http.sys; a prefix without a URL reservation (`netsh http add urlacl`, needs an admin) is refused for a non-admin with `HttpListenerException`, error 5. Research (2026-10-14, secondary sources only): the port number plays no role, the reservation is per prefix; `localhost` is reported to work without admin on Windows 7 / 8.1, where `127.0.0.1` did not; `127.0.0.1` is reported to work on Windows 10 (one remark in googleapis/google-api-dotnet-client#1055, not Microsoft documentation); nothing found about Windows 11. So the old assumption "an IP prefix needs a reservation" may be too pessimistic. Run `login` (SmokeTest or LiveTest) as a standard user on Windows 11 (and 10 if available) with the fixed port and with a fallback port. Error 5 already has its own message and skips the fallback ports; the check should also confirm that the code is 5 on a real machine (`ErrorCode`) and that the `netsh http add urlacl` hint in the message and in the setup guide works. If a normal user is refused for `127.0.0.1`:
  - replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` that reads one request line and writes a fixed response (avoids http.sys and its URL ACLs, and the verbatim Host-header match); the existing `OAuthFlowTests` talk HTTP to the port and keep working. Not `localhost` as a workaround: it needs a second registered redirect URI and the Host-header match gets awkward.
  - If it works, drop the `netsh` hint's "not verified" wording in `docs/inaturalist-setup.md` / `.de.md` and keep the error-5 message for locked-down machines.
- How long the API JWT really lives (assumed 24 h), and that an invalidated JWT is answered with 401 (the publisher renews on 401 only; a 403 is not renewed). Not tested on purpose: what a revoked app authorization answers (the owner will not revoke the production app; try it with a throwaway app if it ever matters).

### Findings from the live test (2026-10-15), to follow up

- The message of `SkippedUnresolvedTaxon` shows the normalized name (`Todo`, `Tten` for `todo`, `TTEN`); consider naming the value as it stands in the file.
- Pacing is per `INaturalistPublisher` / `INaturalistAuthenticator` instance: a host that creates a new publisher per run starts with a fresh pacer (seen as 279 ms between two runs). Make sure the setup guide tells hosts to keep one publisher per session.
- Sound license: `INaturalistOptions.License` sets the license of the observation and the photo (seen working live, 2026-10-15), but the API has no way to set the sound's, so it is the account's default sound license (CC0 on the test account); the setup guide says so. Open idea, not tried: iNaturalist's Rails endpoint `PUT /sounds/:id` permits `license_code`, but it is no documented API endpoint; only worth it if the manual account setting proves too fragile for hosts.

### Taxon names for the BatInspector owner

- `Eptesicus serotinus` is a synonym too (iNaturalist lists *Cnephaeus serotinus*; seen live 2026-10-15, and it is the name in the sample file). Add it to `docs/batinspector-taxa-check.de.md`; the entry is skipped by design, the fix belongs into BatInspector's export.
- Hand over `docs/batinspector-taxa-check.de.md`: `Eptesicus nilssonii` is a synonym (iNaturalist lists *Cnephaeus nilssonii*) and `Myotis oxygnatus` is unknown (*M. oxygnathus* is a synonym of *Myotis blythii* there). Both entries are skipped by design; the fix and the decision about *M. blythii* belong to BatInspector's export.
- BatInspector exports the raw filename abbreviation when it has no species entry (for example `TTEN`, Tadarida teniotis, in its regional lists but not its species list). Such entries are skipped by design; the fix belongs into BatInspector's export.

### iNaturalist validation: verify the rules live

`INaturalistValidator` refuses what iNaturalist would reject; its rules (listed in `docs/inaturalist-setup.md`) come from iNaturalist's open source and forum. Recordings and spectrograms of normal size (2 MB and less) are accepted live; the limits themselves are **not yet verified live**.

- The true upload limit (20 MB or 20 MiB, and the answer to a larger upload): the package refuses files over 20,000,000 bytes by design, so it cannot be tested through it; try it by hand in the web UI if it matters. BatInspector's WAVs are 10x time-expanded (a 383.5 kHz recording has 38.35 kHz in its header; normal), so a high sample rate never reaches iNaturalist and needs no test.
- Test the other rules by hand in the web UI, since the package refuses them before any request: latitude exactly 90, a tag list over the limit. Adjust or drop a rule that iNaturalist does not enforce.
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

The first live test is done (2026-10-15) and its fixes are on `tech/setup-repo`; the remaining live checks above do not block the squash. `main` does not exist yet, so the squash is a root commit:

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

- Prerequisites: the next-day check of the stored login (see "Remaining live checks"), `main` as the default branch and the dry run of `release.yml` (see "GitHub Actions"), a `CHANGELOG.md` section for the version.
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

- Decide what `tools/SmokeTest` is still for now that `tools/LiveTest` exists (it still has `renew`, which needs internals); keep, trim or remove.
- Revisit `net8.0` only if keeping it becomes a burden.
- Versioned serialized result schema: only when BatInspector wants to persist results.
- Re-check iNaturalist API terms (not the rate limits, see the live test) for automated submission.
- Logging: `ActivitySource` and metrics (OpenTelemetry), if a host wants them.
- BatInspector's licence is unclear (checked 2026-10-14): the `LICENSE` file on its `master` branch is CC BY-NC-SA 4.0, `BatInspector/Licenses.txt` says CC BY-NC 4.0, and GitHub detects none. Ask the owner which applies, then replace the "unclear" note in `CLAUDE.md` with the answer. This package contains none of its code, reads only the file format and links to it, so MIT stays possible; keep it that way (a copy of its code would bring the NC / SA terms along). NOTICE: no dependency requires one so far.
- Deprecation policy for schema changes.
