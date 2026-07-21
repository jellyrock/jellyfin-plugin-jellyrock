# JellyRock Companion — agent guide

Server-side Jellyfin plugin (C#, `net9.0`) that is the companion to the JellyRock Roku app. Full
contributor guide: [CONTRIBUTING.md](CONTRIBUTING.md).

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

## Cross-repo

Feature work often spans the Roku app ([jellyrock/jellyrock](https://github.com/jellyrock/jellyrock))
and this plugin; releases must be coordinated (the plugin advertises a required app version). See
[docs/features/](docs/features/).
