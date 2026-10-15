# Releasing

Internal runbook (English only). Never push a commit or a tag without the owner's explicit OK.

## Where things live

| Thing | Lives in | Created by |
|---|---|---|
| The version number | the **git tag** `vX.Y.Z` (no `<Version>` in the csproj) | the maintainer, `git tag -a` |
| Package with that version | nuget.org | release workflow (`dotnet pack`, `dotnet nuget push`) |
| The release page (notes, downloads) | **GitHub Releases**, attached to the tag | release workflow (`gh release create`) |
| Source text of the notes | `CHANGELOG.md` | the maintainer, with every user-visible change |

The release itself is a GitHub object, not a file in the repo. `CHANGELOG.md` is only where the notes are written
down while working; the workflow copies the section of the released version into the GitHub Release body. One source, two places to read it.

The version is derived from the tag by [MinVer](https://github.com/adamralph/minver): tag `v0.1.0` builds package `0.1.0`, tag `v0.2.0-preview.1` builds `0.2.0-preview.1`.
Builds without a tag get `X.Y.(Z+1)-preview.0.N` after a release (`0.1.0-preview.0.N` before the first one), so a dev build can never be mistaken for a release.
The first release, `0.1.0` (2026-10-15), is a stable version by the owner's decision; versions are immutable, so a mistake is fixed in the next one.

## One-time setup (done, keep in step)

| Where | What | Why |
|---|---|---|
| nuget.org, trusted publishing policy | Package owner, scope "Push new packages and package versions" (after the first release: "Push only new package versions"), package `BatInspectorPublisher`, GitHub owner `microglossum`, repository `batinspector-publisher`, workflow file `release.yml`, environment `nuget` | The workflow logs in with OIDC; no API key exists. The policy matches the workflow **file name** and the environment name. |
| GitHub repository secret | `NUGET_USER` = the nuget.org profile name | Input of `NuGet/login`. |
| GitHub environment `nuget` (Settings > Environments) | No required reviewer (a pushed tag publishes on its own). Deployment branches and tags: the tag rule `v*` and nothing else | The job declares `environment: nuget`, which the nuget.org policy requires. Only a pushed `v*` tag can start the job, so only a tag can reach nuget.org; do not add a branch to the list. |
| Ruleset `main` | Pull request and the six CI checks required, no force push, no deletion. The admin bypass lets the owner push to `main` directly, so set its mode to "For pull requests only" if that should not be possible | Work goes through `tech/*` branches and squash-merged pull requests. |

Optional guards: a tag ruleset for `v*` (only the owner may create them), and a required reviewer on the environment (one click before every publish).
Renaming `release.yml` or the environment means changing the nuget.org policy in the same step.

## Describing the size of a release: SemVer

`MAJOR.MINOR.PATCH`. The public surface that counts is the public types of the package and the input file format (`SchemaVersion`).

| Bump | When | Examples here |
|---|---|---|
| **PATCH** `0.1.1` | Bug fix, no public API or schema change | Taxon matching bug, wrong request field, better error message |
| **MINOR** `0.2.0` | Backwards-compatible addition | New option with a default, new `PublishStatus` consumers can ignore, new optional input field, a new adapter such as naturgucker |
| **MAJOR** `1.0.0` -> `2.0.0` | Breaking change | Removing or changing a public type or member, a new required input field or `SchemaVersion` 2 that old files fail on, changed default behavior |

* While in `0.x` anything may change, by convention breaking changes bump MINOR and everything else PATCH. `1.0.0` is the promise that this table is kept.
* Before 1.0 and for risky releases use pre-releases: `0.2.0-preview.1`, later `-rc.1`. Any version containing `-` is marked as a pre-release.
* The size is also visible in `CHANGELOG.md`: group entries under `### Added`, `### Changed`, `### Fixed`, `### Removed`, and start a breaking one with **Breaking:**.
* The input schema version is independent: package `0.3.0` may still read `SchemaVersion` 1 only. A schema change that old files fail on needs a package MAJOR (MINOR in 0.x) and a new `SchemaVersion`.

## Keeping the notes ready (every change)

Add a bullet under `## [Unreleased]` in `CHANGELOG.md` for everything users of the package notice. Internal-only changes need no entry.

## Cutting a release

1. CI is green on `main`; release prerequisites in `TODO.md` are done (the live tests against iNaturalist are in `tools/LiveTest/README.md`; re-run its dry runs before a tag, and the day-later check of the stored login when it is still open).
2. In `CHANGELOG.md` rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a fresh empty `## [Unreleased]` above it.
   Update the compare links at the bottom of the file:
   `[Unreleased]: https://github.com/microglossum/batinspector-publisher/compare/vX.Y.Z...HEAD` and
   `[X.Y.Z]: https://github.com/microglossum/batinspector-publisher/compare/vPREVIOUS...vX.Y.Z` (first release: `.../releases/tag/vX.Y.Z`).
3. Commit `Release X.Y.Z` on a branch `tech/release-X.Y.Z` (create it with `--no-track`, so a plain `git push` cannot land on `main`), push it with `git push -u origin HEAD`, open a pull request and squash-merge it once the checks are green.
   Pre-flight before the tag (read-only): `origin/main` is the release commit, CI on it is green, `CHANGELOG.md` has the `## [X.Y.Z]` section, no tag `vX.Y.Z` exists, and nuget.org does not know the version yet (`curl -s -o /dev/null -w "%{http_code}" https://api.nuget.org/v3-flatcontainer/batinspectorpublisher/index.json` is 404 before the first release).
   Then, on an up-to-date `main`: `git tag -a vX.Y.Z -m "X.Y.Z"` (`git describe --tags` must print `vX.Y.Z`) and `git push origin vX.Y.Z`. Only the owner pushes and tags; nothing is pushed or tagged without their explicit OK.
4. The release workflow (`.github/workflows/release.yml`, starts on the pushed `v*` tag; it checks out with `fetch-depth: 0` so MinVer sees the tag) runs the tests, packs,
   pushes to nuget.org through trusted publishing (`NuGet/login`, secret `NUGET_USER` = profile name, no API key) and creates the GitHub Release with the notes from step 2 and the `.nupkg` / `.snupkg` attached; `-` versions are pre-releases.
   It fails before publishing anything if the tagged commit is not on `main`, if the packed version differs from the tag, or if `CHANGELOG.md` has no section for the tag.
   The job runs in the GitHub environment `nuget` (see "One-time setup"): the trusted publishing policy on nuget.org names it, and only the listed refs can start the job.
   There is no manual or dry run: the workflow starts for a pushed `v*` tag only. Restore, build, test and pack are the same as in CI, which runs them on every pull request and push, and the notes check belongs to the pre-flight (the `awk` command under "Release notes from the changelog" must print the section of the version, not nothing). A change to `release.yml` cannot be tried without a tag; `validate-config` (actionlint) checks it in CI, and a failure before "Push to nuget.org" costs nothing but a new tag.
   Do not register an author-signing certificate on nuget.org: it would make nuget.org reject the unsigned push.
5. Watch the Release run (Actions > Release): checkout, the check that the tagged commit is on `main`, restore, build, test, pack, notes, `NuGet/login`, push, GitHub Release. All green means the package is on nuget.org; it shows "in progress" while nuget.org validates and indexes it (a few minutes, the search API answers 404 until then).
6. Check the GitHub Release (notes, `.nupkg` and `.snupkg` attached, "pre-release" only for `-` versions) and the package page, then install the package from nuget.org into a scratch project on `net8.0` and `net10.0` and call the public API.
7. After the first release only: narrow the trusted publishing policy scope to "Push only new package versions". After every release: groom `TODO.md`, and fix what went wrong in a new version.

If the run fails **before** "Push to nuget.org", nothing was published: delete the tag (`git push origin :refs/tags/vX.Y.Z`, `git tag -d vX.Y.Z`), fix the cause (policy fields, `NUGET_USER`, environment refs, changelog section) and tag again. If it fails **after** the push, the version is out and cannot be replaced: fix forward with the next patch version.

## Release notes from the changelog

```bash
awk -v v="X.Y.Z" '$0 ~ "^## \\[" v "\\]" {f=1; next} f && /^## \[/ {exit} f' CHANGELOG.md > release-notes.md
gh release create "vX.Y.Z" artifacts/*.nupkg artifacts/*.snupkg --title "X.Y.Z" --notes-file release-notes.md   # add --prerelease for -preview/-rc versions
```

GitHub can also generate notes from merged pull requests (`gh release create --generate-notes`, grouped via `.github/release.yml`), but that only lists
PRs, not direct commits, so this project writes the changelog by hand.

## Manual fallback

```bash
dotnet pack src/BatInspectorPublisher -c Release -o artifacts   # on a checkout of the tag
dotnet nuget push "artifacts/*.nupkg" --api-key <KEY> --source https://api.nuget.org/v3/index.json --skip-duplicate
```

Published versions are immutable: they can be unlisted on nuget.org but never deleted or replaced. Fix forward with a new version.
