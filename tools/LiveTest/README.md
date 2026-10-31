# Live test host (black box) - iNaturalist

Console host that uses the **packed** `BatInspectorPublisher` package (public API only) and tests the iNaturalist adapter against the real
iNaturalist. Not part of the solution. Internal doc, English only.

Scope: **iNaturalist only.** Everything platform-specific (login, commands, `scenarios/inaturalist/`) belongs to that adapter. NABU|naturgucker
gets its own `scenarios/naturgucker/` and its own login and commands when its adapter exists; the evidence in `data/` is platform-neutral and shared.

- `scenarios/inaturalist/*.json`: fixed input files with forged places and nights (Hesse, October 2026). Paths are absolute for the devcontainer (`/workspace/...`).
- `data/` (git-ignored): `real/` has PNG/WAV pairs, `malformed/` broken files made once by hand. Credentials: `appsettings.local.json` (here or in `tools/SmokeTest`).
- `out/` (git-ignored): HTTP log (no tokens), `ledger.txt` of everything created. Test observations carry the tag `batinspector-livetest`.

## Setup

Run everything from `tools/LiveTest`. `lt` below stands for `dotnet run -c Release --`.

```text
./pack.sh                  pack the library into ./feed (again after every library change)
lt login                  browser login, once
lt whoami                 shows the stored login
```

## Dry runs (write nothing)

Each prints one line per entry; expected results are listed per file.

```text
lt run scenarios/inaturalist/taxa.json            WouldCreate for bats, genera and group values; SkippedUnresolvedTaxon for synonyms, typos, non-bats
lt run scenarios/inaturalist/time-edge.json       Rejected / warnings for the date rules, SkippedInvalidEntry for 1800
lt run scenarios/inaturalist/validation.json      Rejected for bad positions, types and paths; warnings for sensor errors; SkippedMissingEvidence
lt run scenarios/inaturalist/evidence-bad.json    SkippedInvalidEvidence for 7 broken files; WouldCreate for the last one
```

## Real steps (PUBLIC, irreversible; one at a time)

Add `--commit` to publish. Every created observation is read back and printed, including the licenses of observation, photo and sound.
`--license cc-by-nc` sets `INaturalistOptions.License` (the test observations should not be CC0; the sound stays at the account default, see `docs/inaturalist-setup.md`).
`--geoprivacy obscured|private|open` sets `INaturalistOptions.Geoprivacy`; the read-back is anonymous, so it shows what the public sees (`place_guess`, `location`, `geoprivacy`).

```text
 1. lt run scenarios/inaturalist/smoke.json --commit --license cc-by-nc   Created; licenses: observation=cc-by-nc photo=cc-by-nc sound=<account default>
    open https://www.inaturalist.org/observations/<the uuid printed>        in the browser: does the uuid form of the URL resolve? (decides whether Created can carry a Url)
 2. lt run scenarios/inaturalist/smoke.json                            SkippedDuplicate (right after the write)
 3. lt run scenarios/inaturalist/smoke.json --commit                   SkippedDuplicate

 4. lt run scenarios/inaturalist/resume-sound.json --commit --fail sound   Failed, photo attached, sound not
 5. lt run scenarios/inaturalist/resume-sound.json                     WouldResume
 6. lt run scenarios/inaturalist/resume-sound.json --commit            Resumed

 7. lt run scenarios/inaturalist/resume-photo.json --commit --fail photo   Failed, nothing attached
 8. lt run scenarios/inaturalist/resume-photo.json                     WouldResume
 9. lt run scenarios/inaturalist/resume-photo.json --commit            Resumed

10. lt run scenarios/inaturalist/evidence-refused.json --commit        Failed (iNaturalist itself refuses the photo; its error text is in the message)
11. cp the PNG data/real/Plecotus_*.png over data/malformed/refused.png
12. lt run scenarios/inaturalist/evidence-refused.json                 WouldResume
13. lt run scenarios/inaturalist/evidence-refused.json --commit        Resumed

14. lt run scenarios/inaturalist/time.json --commit                    3x Created; read-back shows the sent clock time and zone (Europe/Berlin, Europe/Lisbon)

15. lt run scenarios/inaturalist/group-species-first.json --commit     2x Created; then without --commit: 2x SkippedDuplicate (found by their tags)
16. lt run scenarios/inaturalist/group-group-first.json --commit       3x Created; Nyctaloid and Mbart keep their species_guess

17. lt run scenarios/inaturalist/geoprivacy-private.json --geoprivacy private --commit   Created; read-back: no place_guess, no location
    (the same with geoprivacy-obscured.json / --geoprivacy obscured: a coarse place_guess, a shifted location, and geoprivacy-open.json / open)
18. lt run scenarios/inaturalist/geoprivacy-private.json --geoprivacy private           SkippedDuplicate (found by the tag, whatever the position)
19. lt run scenarios/inaturalist/resume-sound.json --commit --fail sound --geoprivacy private, then without --commit (WouldResume), then with it (Resumed)
```

## Login edge cases (need the browser)

`--scratch` keeps the normal stored login untouched.

```text
lt login --scratch --occupy-port    port 45679 is taken: a warning, the login falls back to another port; iNaturalist must accept that redirect
lt login --scratch                  click "Deny" in the browser: ends at once with Reason=Denied
lt login --scratch                  do nothing for 5 minutes: ends with Reason=TimedOut
```

## Next day

```text
lt run scenarios/inaturalist/smoke.json    still works without a new login (stored token valid after a day)
```
