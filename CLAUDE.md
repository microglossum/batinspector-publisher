# BatInspectorPublisher

.NET NuGet library `BatInspectorPublisher` (MIT; targets net8.0 + net10.0, SDK 10 pinned in `global.json`) that BatInspector references in-process to publish
bat acoustic-monitoring observations to citizen-science platforms. iNaturalist first; NABU|naturgucker later.
Public repo: <https://github.com/microglossum/batinspector-publisher>.

## Commands

```text
dotnet build                      # whole solution
dotnet test                       # all tests (no network, no live API calls)
dotnet format                     # fix formatting; CI runs `dotnet format --verify-no-changes`
dotnet pack -c Release -o artifacts
```

VS Code tasks (`Terminal > Run Task`) wrap these, plus `check` (format check + build + test). `/check` runs the same from Claude Code.
Before finishing any change: `dotnet format`, then `dotnet test` must pass. CI builds with warnings as errors (`-p:CI=true` reproduces it).

## Architecture

One package, one assembly, two parts. For details read the code; this is only the map.

```text
src/BatInspectorPublisher/
  Core/        platform-neutral: input file, validation, models, results, orchestration, publisher interface
  Adapters/    one folder per platform: everything that platform needs (auth, API client, platform rules, publisher)
tests/         xunit, hand-written HTTP stubs; Fixtures/ = sanitized sample data
tools/SmokeTest/   manual console host for live tests (not in the solution); reads git-ignored appsettings.local.json
```

Flow: BatInspector's export file is read into candidates (one reference recording each), the orchestrator loads the evidence and hands each candidate to a platform publisher, which returns a structured result per candidate.

- `Core` never references `Adapters`, adapters never reference each other (enforced by `ArchitectureTests`). The publisher interface lives in `Core`.
- Each adapter owns its auth entirely. There is no shared auth abstraction; do not assume OAuth outside the iNaturalist adapter.
- Validation has two layers. Platform-neutral checks live in `Core` and reject an entry for every platform; what only one platform rejects (its limits, its accepted ranges) is validated inside that adapter, before any login or request. Never put a platform limit into `Core`.
- naturgucker is blocked (no public API docs): keep it an internal stub, do not guess its shape or bend the publisher interface for it.

## Rules that are decided - do not re-litigate

- Input schema is BatInspector-specific, `SchemaVersion` is required, changes within a version are additive only.
- Validation is per entry: a bad entry is left out and reported, the rest is still published; only a structurally unusable file throws. No strict/lenient switch. Re-runs rely on the duplicate check.
- `PublishOptions.Commit` defaults to false: publishing is public and irreversible, so dry run is the default.
- Taxon resolution is strict and never guesses (no "first autocomplete hit", no broader taxon for a typo). The one exception is a small fixed table of BatInspector group/uncertain values, always on, no option.
- The input file holds one reference recording per species, night and location, not every detection; the duplicate check relies on that on purpose.
- The package ships no credentials and reads no config files or environment variables. The host passes the adapter options.
- Decided against:
  - rolling back (deleting) an observation whose evidence could not be attached (2026-10-03: deleting public data automatically is destructive and the failure may be transient; resume completes it on the next run);
  - geoprivacy / sensitive-species handling (2026-10-02: iNaturalist obscures sensitive taxa itself, other platforms may not support it, the package cannot solve it);
  - pattern-matching or translating iNaturalist's error messages (2026-10-04: the texts are undocumented and can change, so such code would break silently; the platform's message is passed through, and clear messages come from the adapter's own validation before anything is written);
  - an offline validation pass over all candidates before the first one is published (2026-10-04: the host publishes in batches, not all at once, so a bad entry late in the file costs at most one batch, and the dry run already finds it).

## Conventions

- Code, comments, exceptions, logs, tests: English. User-visible content posted to a platform (the observation description) is German and built in the adapter.
- Docs are Markdown. User-facing docs are bilingual: English is the source (`README.md`, `docs/*.md`), German is `*.de.md`; change both together. Small files (`CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`) hold both languages in one file.
- Library code never writes to the console. Use `ILogger` (optional, NullLogger default) and structured results.
- Never log tokens or token-bearing response bodies.
- Public API is minimal and fully XML-documented; wire models and API clients stay `internal`.
- Tests use hand-written HTTP stubs, never live APIs. Windows-only behavior uses `[WindowsOnlyFact]`.
- Keep `CHANGELOG.md` (Keep a Changelog) updated under `[Unreleased]`.
- The library multi-targets `net8.0;net10.0`. Do not use APIs that exist only on net10.0 without a `#if`.

## Secrets and data - hard rules

- Never read, print, copy or commit `*.local.json`, `.env*`, `local/` (real observation data), tokens or client secrets. Permissions deny reading them.
- Fixtures in `tests/**/Fixtures` must be sanitized: no real coordinates, no real local paths.
- Do not `git push` or commit unless the user explicitly asks. The remote is public.

## Backlog and docs

- `TODO.md` is the project backlog: open and blocked work only, no list of finished items. Read it at the start of a task, update it when something is decided or found, and groom it before every commit (see Git rules). English only.
  Finished work is recorded by the commit and `CHANGELOG.md`; a decision against something goes into the "decided" rules above, not into `TODO.md`.
- `CLAUDE.md` (this file) holds standing rules and the architecture map, not details the code already shows; `docs/releasing.md` is the release runbook. Claude's own auto-memory lives outside the repo and is per machine.
- Internal docs (`CLAUDE.md`, `TODO.md`, `CHANGELOG.md`) are English only; user-facing docs are bilingual.

## Project stance

One-person project: external code contributions are not solicited (bug reports are welcome, a contributed new platform adapter would be the exception). Keep community files minimal; do not add contributor-facing process.

## Git rules

- **No AI attribution.** Commit messages and PR descriptions must not mention Claude or AI and must not contain `Co-Authored-By: Claude` or "Generated with Claude Code" lines. Enforced by `"attribution": {"commit": "", "pr": ""}` in `.claude/settings.json`; if a session or tool still suggests such a line, leave it out.
- Branches: `main` is the squash-merge target; do not commit on it directly. Work on a branch such as `tech/<topic>` and squash-merge into `main` when it is good.
- Commit messages are English and explain **why**, not what (the diff shows what):
  - Subject: imperative, at most 72 characters, no trailing period, no `feat:`/`fix:` prefix (the changelog is hand-written, nothing parses prefixes).
  - Body (wrapped at 72): why the change exists (problem or constraint); the decisions taken and the alternatives rejected, with the reason; consequences and open ends (what is unverified, deferred or known to be imperfect). No file-by-file list.
  - Issue references as trailers at the end: `Refs: #12` for related work, `Closes: #12` only on the squash commit that lands on `main`. Do not reference `TODO.md` sections (they get renamed); put the decision itself in the body. Once backlog items become issues, each TODO entry carries its issue number.
  - The squash commit on `main` is the permanent record: write it carefully. Commits on `tech/*` branches are working history and may be shorter.
  - Public repo: no tokens, real coordinates, local paths, usernames or e-mail addresses in a message.
- The commit identity is configured repo-locally (`.git/config`): the owner's name and their GitHub noreply address. Never use or write the owner's real e-mail address anywhere. Do not touch `git config --global`.
- Never `git push`, tag or commit without the owner's explicit OK (settings ask for confirmation).
- **Before every commit, groom `TODO.md` - always, no exception.** Remove what the commit finishes (there is no Done list: the commit and `CHANGELOG.md` are the record), cut a section down to its remaining items when only part is done and never describe finished work inside an open section,
  and add what was decided or found along the way. Do this before `git commit`, and say in the reply what changed in `TODO.md`. A commit with a stale `TODO.md` is not ready.

## Versioning

The package version comes from the git tag via MinVer (`vX.Y.Z`); never add `<Version>` to the csproj. Release procedure and SemVer sizing: `docs/releasing.md`.
Validate non-C# files with `scripts/validate-config.sh` (VS Code task "validate (config files)"; the tools come from the devcontainer) before finishing changes to workflows, YAML/JSON config, Markdown or the devcontainer.

## BatInspector (producer of the input file)

BatInspector is open source: <https://github.com/chrmue44/BatInspector>. Look there to learn what it writes (field meanings, species list); `gh` is not logged in, so use `curl` on `api.github.com` / `raw.githubusercontent.com`.
The public code can lag behind: a feature or integration may sit on a non-`main` branch or not be pushed at all. If something is missing or unclear, say so and ask the owner instead of guessing. BatInspector is CC BY-NC 4.0: read it for facts, never copy its code into this MIT repo.
The old prototypes live on in the owner's other repo; do not look for them here.
