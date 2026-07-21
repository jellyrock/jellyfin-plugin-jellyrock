# Contributing

## Commit messages & PR titles

This repo uses [Conventional Commits](https://www.conventionalcommits.org/). The format is:

```
<type>[(scope)][!]: <description>
```

**This isn't bureaucracy — the type drives the changelog.** `CHANGELOG.md` is generated
automatically from commit subjects (see below), so the type you choose is the section your
change lands in. A subject with no recognized type silently defaults to **Changed**.

| Type | Changelog section |
|------|-------------------|
| `feat` | **Added** |
| `fix` | **Fixed** |
| `perf`, `refactor`, `revert` | **Changed** |
| `chore`, `ci`, `build`, `test`, `style`, `docs` | *(skipped — housekeeping)* |

Examples:

```
feat(cold-cast): advertise closed devices as Play On targets
fix: stop the reaper from advancing the saved resume position
docs: correct the cold-launch cast app-version floor
ci: add the commit-style gate
```

### Where the changelog entry actually comes from

- **Squash-merged PR** (the norm) → the **PR title** becomes the commit subject. So the *PR title*
  is what must be conventional — that's what the CI gate checks.
- **Merge-commit PR** → the PR title is resolved via `gh` (same result).
- **Direct commit to `main`** → the commit subject itself.

Pick the type deliberately: a new user-facing feature is `feat` (Added), not a vague default.

### Enforcement

- **PRs:** the **Commit Style** workflow validates the PR title and blocks the merge if it isn't
  conventional.
- **Direct pushes to `main`:** the same workflow validates the pushed subjects after the fact (a red
  run is the signal — github.com can't reject a push by message).
- **Locally**, check any subject before committing — no toolchain needed:

  ```bash
  scripts/lint-commit-subject.sh "feat(cold-cast): wake a closed Roku over ECP"
  ```

## The changelog is automated — don't hand-edit it

`CHANGELOG.md`'s `## [Unreleased]` section is **regenerated on every push to `main`** and committed
back by the bot. Manual edits to it will be overwritten. To change what appears, change the commit
subject (see above). Dated release sections are written by the release flow. See
[docs/dev/releasing.md](docs/dev/releasing.md).

## Building & releasing

No local .NET SDK is required — builds run in the official SDK container. See
[docs/dev/README.md](docs/dev/README.md) for the build/test commands and
[docs/dev/releasing.md](docs/dev/releasing.md) for the release runbook.
