> **Canonical agent-instruction file.** This file (AGENTS.md) is the single real instruction file for every agent/tool in this repo. CLAUDE.md is a symlink to it. Always edit AGENTS.md; never create a standalone CLAUDE.md or let the two diverge. Any reference elsewhere in this repo to "CLAUDE.md" means this file.

# JellyRock Companion — agent guide

Server-side Jellyfin plugin (C#, `net9.0`) that is the companion to the JellyRock Roku app. Full
contributor guide: [docs/dev/contributing.md](docs/dev/contributing.md).

## Commits & PR titles — conventional, because they drive the changelog

`CHANGELOG.md` is auto-generated from commit subjects, so **every commit subject and PR title must be
a conventional commit**: `<type>[(scope)][!]: <description>`.

- Type → changelog section: `feat` → **Added**, `fix` → **Fixed**, `perf`/`refactor`/`revert` →
  **Changed**, `chore`/`ci`/`build`/`test`/`style`/`docs` → *skipped*.
- **Choose the type deliberately** — a new user-facing feature is `feat`, not a prefix-less subject
  (which silently defaults to Changed). This is exactly how #9 first landed miscategorized.
- The **PR title** is the changelog source for squash/merge PRs; the commit subject for direct pushes.
- Validate before committing: `scripts/lint-commit-subject.sh "<subject>"`. CI (**Commit Style**) also
  gates PR titles.

## CHANGELOG.md is machine-managed — never hand-edit it

`## [Unreleased]` is regenerated on every push to `main` and committed back by `jellyrock[bot]`; manual
edits are overwritten. To change an entry, fix the commit subject. Release dating happens in the
release flow ([docs/dev/releasing.md](docs/dev/releasing.md)).

## Build

No host .NET SDK — build/test in the SDK container (commands in [docs/dev/README.md](docs/dev/README.md)).

## Work that spans the app and the plugin

Feature work often spans the Roku app ([jellyrock/jellyrock](https://github.com/jellyrock/jellyrock))
and this plugin; releases must be coordinated (the plugin advertises a required app version). See
[docs/features/](docs/features/).

## Repo facts

The facts the shared skills read, one slot each (they link `AGENTS.md#<slot>`).

### Capture types

decision, followup

### Public posture

`public`: committed text names no private repository or host, no person, email or home path, and no secret.

### Docs check

none

### Verification commands

- The test project in the SDK container (the `docker run … dotnet test` command in [docs/dev/README.md](docs/dev/README.md#test)): the floor after any code change. CI's **Build + test** job runs the same tests.
- `scripts/check-server-abi.sh`: the plugin still binds to every supported Jellyfin server line. Run it after a change to which server types or members the plugin uses.
- `scripts/lint-commit-subject.sh "<subject>"`: every commit subject and PR title, before committing.
- `bash .claude/skills/log/voice-check.sh [<file.md>…]`: the house voice of `.claude/rules/writing-style.md`, per tracked Markdown file against `.voice-baseline.tsv` (a file may only go down). Run it after editing Markdown; CI's **House voice** job runs it on every file. Reword a new hit, never raise the baseline; `--lower` locks in a file's gain.
- Never edit `CHANGELOG.md` to make a check pass: it is regenerated from commit subjects.

### Plan path

`~/.claude/plans/`

### Forge

backend: github
prs: always
