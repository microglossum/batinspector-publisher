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

The version is derived from the tag by [MinVer](https://github.com/adamralph/minver): tag `v0.1.0-preview.1` builds package `0.1.0-preview.1`.
Builds without a tag get `0.1.0-preview.0.N` (or `X.Y.(Z+1)-preview.0.N` after a release), so a dev build can never be mistaken for a release.

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

1. CI is green on `main`; release prerequisites in `TODO.md` are done (live test against iNaturalist).
2. In `CHANGELOG.md` rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and add a fresh empty `## [Unreleased]` above it.
   Update the compare links at the bottom of the file:
   `[Unreleased]: https://github.com/microglossum/batinspector-publisher/compare/vX.Y.Z...HEAD` and
   `[X.Y.Z]: https://github.com/microglossum/batinspector-publisher/compare/vPREVIOUS...vX.Y.Z` (first release: `.../releases/tag/vX.Y.Z`).
3. Commit `Release X.Y.Z`, create the annotated tag `git tag -a vX.Y.Z -m "X.Y.Z"`, push the commit and the tag (owner's OK first).
4. The release workflow (to be written, see `TODO.md`; it checks out with `fetch-depth: 0` so MinVer sees the tag) packs, pushes to nuget.org
   and creates the GitHub Release with the notes from step 2 and the `.nupkg` attached; `-` versions are pre-releases.
5. Check the package page on nuget.org (indexing takes a few minutes) and install it into a scratch project.

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
