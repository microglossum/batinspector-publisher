# TODO

Project backlog, versioned with the code. English only (internal doc, no German twin).
It lists work that is still open or blocked, and nothing else: finished work is recorded by the commit and
`CHANGELOG.md`, decisions against something live in `CLAUDE.md`. Read this before starting work and groom it
before every commit (rules in `CLAUDE.md`).

## Open: questions for the BatInspector owner and engineers

- **Group values per night and place.** Can the export hold several entries with a group value for one night and place, the same value twice (two `Nyctaloid`) or different ones (`Nyctaloid`, `Social`, `?`)? All group values of a kind share the taxon (Chiroptera), so only the first one published survives. Matching also on `species_guess` would separate `Nyctaloid` from `Social`, but not two `Nyctaloid`. If several are wanted, the rule "one reference recording per species, night and place" needs an explicit exception for group values. Answer first, then decide.
- **Taxon names to correct in the export** (list: `docs/batinspector-taxa-check.de.md`; entries are skipped by design, the fix belongs into BatInspector):
  - Add `Eptesicus serotinus` (iNaturalist lists *Cnephaeus serotinus*) to that document; it is the name in the sample file.
  - `Eptesicus nilssonii` is a synonym (*Cnephaeus nilssonii*); `Myotis oxygnatus` is unknown (*M. oxygnathus* is a synonym of *Myotis blythii* there), and the decision about *M. blythii* is BatInspector's.
  - BatInspector exports the raw filename abbreviation when it has no species entry (for example `TTEN`, Tadarida teniotis).
- **BatInspector's licence is unclear:** the `LICENSE` file on its `master` branch says CC BY-NC-SA 4.0, `BatInspector/Licenses.txt` says CC BY-NC 4.0, and GitHub detects none. Ask which applies, then replace the "unclear" note in `CLAUDE.md` with the answer. This package contains none of its code and only reads the file format, so MIT stays possible; keep it that way. No dependency requires a NOTICE file so far.
- A WAV over 20,000,000 bytes is refused with no way out. Can BatInspector export a shorter or resampled copy?

## Open: code and design

### Live checks against iNaturalist that are still open

- **Stored login after a day or more:** run `tools/LiveTest/scenarios/inaturalist/smoke.json` as a dry run on a later day. It must not ask for a browser login. It also shows the silent renewal after the API token (assumed to last 24 h) has expired; that an invalid token is answered with 401 stays an assumption.
- **Rate limits, 429 and retries** (defaults `MaxAttempts = 1`, `MinRequestInterval` 1 s): the limits come from the forum and secondary sources (100 requests per minute, 60 asked for, about 10,000 per day). Read iNaturalist's "API recommended practices" page in a browser (it answers 403 to our tools) and compare with `docs/inaturalist-setup.md`. A 429, a `Retry-After` header and the write limits have not been observed and are not to be provoked: no rate-limit response header has appeared so far. Use the findings to document a sensible `MaxAttempts` and `RetryBaseDelay` for hosts that opt in, and to confirm that the enforced 1 s minimum for `MinRequestInterval` matches the limit.
- **Login as a non-admin Windows user** (BatInspector's normal case; CI's Windows runners run as admin). `OAuthFlow` registers `http://127.0.0.1:<port>/` with `HttpListener` (http.sys); a prefix without a URL reservation is refused for a non-admin with error 5 (`HttpListenerException`). Secondary sources suggest `127.0.0.1` may work without admin on Windows 10 (one remark in googleapis/google-api-dotnet-client#1055); nothing found about Windows 11. Run `login` as a standard user on Windows 11 (and 10) with the fixed port and with a fallback port, and confirm that the error code on a real machine is 5 (`ErrorCode`) and that the `netsh http add urlacl` hint in the message and in the setup guide works. If a normal user is refused for `127.0.0.1`, replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` that reads one request line and answers with a fixed response (no http.sys, no Host-header match; the `OAuthFlowTests` talk HTTP to the port and keep working). If it works, drop the "not verified" wording about the `netsh` hint in `docs/inaturalist-setup.md` / `.de.md`.
- **Login with `localhost` or `[::1]` as redirect host:** the options accept both, the docs recommend `127.0.0.1`, the only form that was tried.
- **Rules that only the web UI can check**, since the package refuses them before any request: the true upload limit (20 MB or 20 MiB, and the answer to a larger upload), latitude exactly 90, and a tag list over the limit. Adjust or drop a rule that iNaturalist does not enforce. The rules in `INaturalistValidator` are from iNaturalist's open source and forum; uploads of normal size (2 MB and less) are accepted.

### Follow-ups in the package and its docs

- The message of `SkippedUnresolvedTaxon` shows the normalized name (`Todo`, `Tten` for `todo`, `TTEN`); name the value as it stands in the file.
- Pacing is per `INaturalistPublisher` / `INaturalistAuthenticator` instance, so a host that creates a new publisher per run starts with a fresh pacer. Tell hosts in the setup guide to keep one publisher per session.
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

### After the first release (0.1.0)

- Optional guards: a tag ruleset for `v*` that blocks updates and deletions (nobody can move a released tag; to delete a tag after a failed run the ruleset has to be switched off briefly), a required reviewer on the environment `nuget`, and the bypass mode of the `main` ruleset set to "For pull requests only" (the owner can push to `main` directly now).
- Optional: reserve the ID prefix `BatInspectorPublisher` (not `BatInspector*`, which would also claim the BatInspector project's IDs) by e-mail to `account@nuget.org`.

### GitHub Actions and repository

- Check on the next pull request that the gitleaks job works with the workflow-level `permissions: contents: read` (the action may need `pull-requests: read`).
- Dependabot must keep the SHA-pinned actions (`# vN` comments) current; check its next pull requests.
- Remove `.github/actionlint.yaml` once a released actionlint knows the `ubuntu-26.04` label.
- The actionlint version in the `validate-config` job is pinned by hand (Dependabot does not see it): bump it together with the devcontainer feature now and then.
- Optional: a musl test leg in an Alpine job container (`mcr.microsoft.com/dotnet/sdk:10.0-alpine`), only if a consumer runs on Alpine.
- Optional: `CITATION.cff` if the tool should be citable.

### NuGet package quality

- Optional: replace the placeholder icon `assets/icon.png` (128x128, transparent corners) with a better one, and remake `assets/social-preview.png` from it (it is scaled up from the 128 px icon, so its edges are soft) and upload it again.
- Later: `PackageValidationBaselineVersion` once a stable version exists (it compares against a stable release, so a `0.x` preview has none); `Microsoft.CodeAnalysis.PublicApiAnalyzers` only if the `ArchitectureTests` allowlist stops being enough (it already fails on an unplanned public type, but does not track members).
- Not researched yet: trimming and AOT annotations, SBOM, an API docs site (DocFX on GitHub Pages, generated from the XML docs).

## Blocked

### Implement NABU|naturgucker integration

Blocked: there is no public API documentation, so the auth and submission model is unknown.

- Identify and contact NABU/naturgucker developers: API (REST?), auth (API key, OAuth, basic, manual file import?), rate limits,
  terms for automated submission, whether this bat-monitoring use case is welcome.
- Write `docs/naturgucker.md` (bilingual, like `docs/inaturalist.md`) together with the adapter, and a `scenarios/naturgucker/` set for `tools/LiveTest`.
- Then implement `Adapters/Naturgucker` (currently an internal stub) with its own auth, and only then revisit
  `IObservationPublisher` before 1.0. Do not guess its shape in the meantime.
  The public types to revisit then are the iNaturalist-shaped parts of the result model: `PublishStep` (iNaturalist's
  order: create, then attach the photo, then the sound), `PublishResult.SpectrogramAttached` / `AudioAttached` (on a
  platform without sound upload every `Created` would look incomplete) and `WouldResume` / `Resumed` (exist only because
  iNaturalist's submission takes several requests). Do not redesign them before then: with one platform any redesign is
  a guess.

## Later / carried over

- Decide what `tools/SmokeTest` is still for now that `tools/LiveTest` exists (it still has `renew`, which needs internals); keep, trim or remove.
- Sound license: the API has no way to set it, so it is the account's default (documented in the setup guide). Only if that manual setting proves too fragile for hosts: iNaturalist's Rails endpoint `PUT /sounds/:id` permits `license_code`, but it is no documented API endpoint.
- A revoked app authorization is untested (it needs a throwaway OAuth application).
- Re-check iNaturalist's API terms for automated submission.
- Revisit `net8.0` only if keeping it becomes a burden.
- Versioned serialized result schema: only when BatInspector wants to persist results.
- Logging: `ActivitySource` and metrics (OpenTelemetry), if a host wants them.
- Deprecation policy for schema changes.
