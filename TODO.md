# TODO

Open and blocked work only, short on purpose. Finished work is in the commits and `CHANGELOG.md`, decisions against something in `CLAUDE.md`.
Read it before starting work and groom it before every commit (rules in `CLAUDE.md`).

## Important now

1. **Login as a normal Windows user** (BatInspector's usual case; CI's Windows runners are admins). `OAuthFlow` uses `HttpListener`, which may refuse a non-admin with error 5 (`HttpListenerException`). Run `login` as a standard user on Windows 11 with the fixed port and with a fallback port. If `127.0.0.1` is refused, replace `HttpListener` with a small `TcpListener` on `IPAddress.Loopback` (the `OAuthFlowTests` talk HTTP to the port and keep working); if it works, drop the "not verified" wording about the `netsh` hint in `docs/inaturalist-setup.md` / `.de.md`. The same run shows the DPAPI token store on a real machine. BatInspector's developers have run the package on a Windows machine and it seems to work (2026-10-31); it is not rolled out to all users yet, and it is not known whether that was a standard user or an admin, so ask before dropping this item.
2. **Answers from BatInspector's owner and engineers:**
   - Can the export hold several group-value entries (`Nyctaloid`, `Social`, `?`) for one night and place? They share the taxon Chiroptera, so only the first is published. If several are wanted, the rule "one reference recording per species, night and place" needs an exception for group values.
   - Fix the taxon names in the export (list: `docs/batinspector-taxa-check.de.md`; add `Eptesicus serotinus` there): `Eptesicus serotinus` and `E. nilssonii` are synonyms (*Cnephaeus*), `Myotis oxygnatus` is unknown, raw codes such as `TTEN` appear. The entries are skipped by design.
   - Which licence applies to BatInspector (CC BY-NC-SA 4.0 in `LICENSE`, CC BY-NC 4.0 in `Licenses.txt`)? Then replace the "unclear" note in `CLAUDE.md`.
   - Can the export shorten or resample a WAV over 20,000,000 bytes? The package refuses it.
3. **Stored login after a day:** run `tools/LiveTest/scenarios/inaturalist/smoke.json` as a dry run on a later day. It must not ask for a browser login.

## Soon

- **Coordinate privacy (owner's request, 2026-10-29):** let the user hide the exact position, e.g. of a roost. iNaturalist offers geoprivacy `open` / `obscured` / `private`; the file would carry a top-level option next to `SchemaVersion` (additive, so no new schema version). Reopens the "geoprivacy" line under "Decided against" in `CLAUDE.md`: that decision was about handling sensitive species, this is a setting the user chooses. When it is taken up, replace that line with what is decided and update `docs/inaturalist.md` / `.de.md` ("Not sent: ... geoprivacy", "exact coordinates") and `docs/input-format.md` / `.de.md`. Researched 2026-10-29: feasible. The v2 create schema (`openapi/schema/request/observations_create.js` in iNaturalist's `iNaturalistAPI`) has `observation.geoprivacy` (a plain string); the values are `open` / `obscured` / `private` as in the v1 search filter. Not yet written live, so the exact accepted values and whether `place_guess` is kept private are unverified. Our `place_guess` is the coordinates as text, so for `obscured` / `private` its text must be neutral (the field stays, v2 rejects an empty value; the description holds no coordinates): sent as is it would reveal the position. iNaturalist may hide `place_guess` itself (public obscured observations show a place name, not a text of the owner's), but that is unverified, so the adapter does not rely on it. Open points:
  - header-wide only (the owner's wish) or also per entry;
  - neutral values in `Core` (e.g. open / obscured / private) mapped inside the iNaturalist adapter, since naturgucker may not offer them (a platform that cannot honour a non-open value must reject the entry, never publish it open);
  - absent means today's behaviour (nothing sent), an unknown value rejects the whole file or the entry (decide which, per the per-entry validation rule);
  - the dry run and the result should show the chosen privacy;
  - `private` hides the position even from the owner's map: check what iNaturalist then shows for the sound and the spectrogram, and whether `place_guess` still leaks the place;
  - a live test scenario with the forged places (owner approves each `--commit`).
- **Duplicates across users (request from BatInspector's side, 2026-10-29; the owner doubts it is wise): decide whether to do it at all.** Goal: do not publish a recording that another BatInspector user already published. Today the check only looks at the logged-in user's own observations (same taxon, same day, 100 m). Notes for the decision:
  - A search over all users with the radius is unreliable: other users' observations may be obscured (cell of about 0.2°) or private (not returned), so it misses what it should catch, and it also catches what it should not (two recorders at the same roost on one night are two observations, not a duplicate). A false hit blocks a legitimate upload.
  - A narrower idea: ask for the `batinspector:` hash tag of the entry across all users. It is set only by this package, hashes species, instant and coordinates, and stays visible when the position is obscured, so it finds the same recording published twice and nothing else. Only observations from this package (since 0.1.0) carry it, and a user can edit the tags away.
  - Researched 2026-10-29 with anonymous read-only requests: `GET /v1/observations?q=batinspector:<12 hex>&search_on=tags` matches the whole tag exactly (no prefix, no partial token), across all users, and also returns obscured and private observations, with their tags. The radius search does not: a private observation has no `location`, an obscured one a shifted one. There is no dedicated tag parameter, only `q` + `search_on`. Checked with tags of other observations: the tag search also finds an obscured and a private one (the private one has no `location` and is still found). No observation with a `batinspector:` tag is obscured or private yet, so the combination itself is untested; test it with the forged places. 46 public observations of an early user of the package carry `batinspector:<12 hex>` tags (the owner's statement); compare one hash with the package's computation before relying on it.
  - A hit on another user's observation cannot be resumed (it is not ours): it needs its own result status or at least a clear message, not `SkippedDuplicate` as is. Also one more request per candidate, within the pacing.
  - Leaning: no radius search over all users; the tag lookup only if two users really upload the same files. Ask the BatInspector side whether that happens (shared datasets) before building anything.
- Rate limits and terms: read iNaturalist's "API recommended practices" in a browser (it answers 403 to our tools), compare with `docs/inaturalist-setup.md`, re-check the API terms for automated submission, and document sensible `MaxAttempts` / `RetryBaseDelay` values. A 429 has never been seen.
- Check by hand in the iNaturalist web UI what the package refuses before any request: the true upload limit (20 MB or 20 MiB), latitude exactly 90, a tag list over the limit. Drop a rule that iNaturalist does not enforce.
- Dependabot: look at its first pull requests against `main` (SHA-pinned actions); bump the hand-pinned actionlint version now and then; remove `.github/actionlint.yaml` once actionlint knows the `ubuntu-26.04` label.
- Decide what `tools/SmokeTest` is still for now that `tools/LiveTest` exists (it still has `renew`): keep, trim or remove.
- Optional: a tag ruleset for `v*` that blocks updates and deletions, a required reviewer on the environment `nuget`, the bypass mode of the `main` ruleset set to "For pull requests only", reserving the ID prefix `BatInspectorPublisher` (by e-mail to `account@nuget.org`).

## Blocked

- **NABU|naturgucker:** blocked on NABU's written OK and an app identifier for BatInspector (the owner asks for both). The adapter stays the empty stub until then. Plan once they are in:
  - `Adapters/Naturgucker` with its own options and auth: the host passes the app identifier and the user's login, the package ships neither; no stored token. Hosts get no way to weaken a check, as in the iNaturalist adapter.
  - Species are resolved live by name and never cached. Bats only: the adapter checks the genus against a small fixed list of bat genera before any request (a platform rule, so inside the adapter). Fail closed: an unknown genus is skipped and reported, never published. Ask NABU whether a species' group can be queried; that would replace the list. Check whether the platform has an entry for the BatInspector group values.
  - Evidence: check which types the platform takes (BatInspector's spectrograms are PNG, the recordings WAV). What it cannot take is not published and gets its own result. Then revisit `IObservationPublisher` and the iNaturalist-shaped result types (`PublishStep`, `SpectrogramAttached` / `AudioAttached`, `WouldResume` / `Resumed`) before 1.0.
  - Duplicate check and resume through the user's own observations on the platform; check whether an identifier of our own per observation can serve as the key.
  - Privacy: map the neutral setting of "Coordinate privacy" (under "Soon") to what the platform offers; an entry the platform cannot honour is rejected, never published open.
  - There is no test system. Unit tests use HTTP stubs with made-up values. Live tests first read-only (login, species lookup, duplicate query) as `scenarios/naturgucker/` in `tools/LiveTest`. The first write is one real entry from the owner's recordings, run with `--commit` after the owner's OK; before that, check that the owner can delete an observation in the platform's web UI.
  - Docs: `docs/naturgucker.md` / `.de.md`, the owner reviews the wording before it is committed. Then CHANGELOG and the platform list in the READMEs.

## Later, when something triggers it

- BatInspector runs off Windows: real secret storage per OS (the host supplies an `INaturalistTokenStore`; no shared abstraction before a second adapter shows what it needs).
- A stable version needs a baseline: `PackageValidationBaselineVersion`.
- A better icon: remake `assets/social-preview.png` and upload it again.
- The account's sound license proves too fragile for hosts: iNaturalist's Rails endpoint `PUT /sounds/:id` permits `license_code` (not a documented API endpoint).
