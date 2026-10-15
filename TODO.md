# TODO

Open and blocked work only, short on purpose. Finished work is in the commits and `CHANGELOG.md`, decisions against something in `CLAUDE.md`.
Read it before starting work and groom it before every commit (rules in `CLAUDE.md`).

## Important now

1. **Login as a normal Windows user** (BatInspector's usual case; CI's Windows runners are admins). `OAuthFlow` uses `HttpListener`, which may refuse a non-admin with error 5 (`HttpListenerException`). Run `login` as a standard user on Windows 11 with the fixed port and with a fallback port. If `127.0.0.1` is refused, replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` (the `OAuthFlowTests` talk HTTP to the port and keep working); if it works, drop the "not verified" wording about the `netsh` hint in `docs/inaturalist-setup.md` / `.de.md`. The same run shows the DPAPI token store on a real machine.
2. **Answers from BatInspector's owner and engineers:**
   - Can the export hold several group-value entries (`Nyctaloid`, `Social`, `?`) for one night and place? They share the taxon Chiroptera, so only the first is published. If several are wanted, the rule "one reference recording per species, night and place" needs an exception for group values.
   - Fix the taxon names in the export (list: `docs/batinspector-taxa-check.de.md`; add `Eptesicus serotinus` there): `Eptesicus serotinus` and `E. nilssonii` are synonyms (*Cnephaeus*), `Myotis oxygnatus` is unknown, raw codes such as `TTEN` appear. The entries are skipped by design.
   - Which licence applies to BatInspector (CC BY-NC-SA 4.0 in `LICENSE`, CC BY-NC 4.0 in `Licenses.txt`)? Then replace the "unclear" note in `CLAUDE.md`.
   - Can the export shorten or resample a WAV over 20,000,000 bytes? The package refuses it.
3. **Stored login after a day:** run `tools/LiveTest/scenarios/inaturalist/smoke.json` as a dry run on a later day. It must not ask for a browser login.
4. **Two small fixes:** the `SkippedUnresolvedTaxon` message shows the normalized name (`Todo` for `todo`), name the value as in the file; the setup guide should say that a host keeps one `INaturalistPublisher` per session (the pacer is per instance).

## Soon

- Rate limits and terms: read iNaturalist's "API recommended practices" in a browser (it answers 403 to our tools), compare with `docs/inaturalist-setup.md`, re-check the API terms for automated submission, and document sensible `MaxAttempts` / `RetryBaseDelay` values. A 429 has never been seen.
- Check by hand in the iNaturalist web UI what the package refuses before any request: the true upload limit (20 MB or 20 MiB), latitude exactly 90, a tag list over the limit. Drop a rule that iNaturalist does not enforce.
- Dependabot: look at its first pull requests against `main` (SHA-pinned actions); bump the hand-pinned actionlint version now and then; remove `.github/actionlint.yaml` once actionlint knows the `ubuntu-26.04` label.
- Decide what `tools/SmokeTest` is still for now that `tools/LiveTest` exists (it still has `renew`): keep, trim or remove.
- Optional: a tag ruleset for `v*` that blocks updates and deletions, a required reviewer on the environment `nuget`, the bypass mode of the `main` ruleset set to "For pull requests only", reserving the ID prefix `BatInspectorPublisher` (by e-mail to `account@nuget.org`).

## Blocked

- **NABU|naturgucker:** there is no public API documentation. Contact NABU first (API, auth, rate limits, terms for automated submission). Then write `docs/naturgucker.md`, implement `Adapters/Naturgucker` with its own auth, add `scenarios/naturgucker/` to `tools/LiveTest`, and only then revisit `IObservationPublisher` and the iNaturalist-shaped result types (`PublishStep`, `SpectrogramAttached` / `AudioAttached`, `WouldResume` / `Resumed`) before 1.0. Do not guess their shape before.

## Later, when something triggers it

- BatInspector runs off Windows: real secret storage per OS (the host supplies an `INaturalistTokenStore`; no shared abstraction before a second adapter shows what it needs).
- A stable version needs a baseline: `PackageValidationBaselineVersion`.
- A better icon: remake `assets/social-preview.png` and upload it again.
- The account's sound license proves too fragile for hosts: iNaturalist's Rails endpoint `PUT /sounds/:id` permits `license_code` (not a documented API endpoint).
