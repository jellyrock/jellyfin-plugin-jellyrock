# Cursor

Live state that survives between Claude Code sessions: in-flight non-project work, open followups, background jobs currently running, and the recurring-attention cadences. This is the "where am I, what's queued, what's due?" surface `/catchup` reads first.

Tracked projects are kept outside this repo, on the maintainer's machine (`docs/projects/` is gitignored); they have their own PLANs, statuses, and lifecycle skills (`/start-project` / `/resume-project` / `/end-session`). This cursor doc is for everything else: ad-hoc work between projects, followups captured via `/log followup` for next session, and the cadences that aren't crontabbed but still need human attention.

**Last updated**: 2026-10-27

## In-flight non-project work

Nothing currently in flight outside the tracked projects.

## Open followups

> Captured via `/log followup`, closed via `/done`, both through `.claude/skills/log/journal.sh`. Group by category (one `### Category` heading per area). Each followup is a `####` heading ending in its `[fid: …]` and `[captured …]` markers, with its body below it; refer to one by its fid, never by its position. Keep each a self-contained "what + why + where" so a future session can act on it cold.

None open.

### CI

#### The release workflows have no rows in /ci-triage's gates.tsv `[fid: release-workflows-unmapped-in-ci-gates]` `[captured 2026-10-27]`

`.claude/skills/ci-triage/gates.tsv` maps every step of `ci.yml`, `commit-style.yml`, `changelog-validate.yml`, `changelog-sync.yml` and `server-abi-watch.yml`, but not `release.yml` or `release-prepare.yml` (packaging with jprm, the tag, the GitHub release, the gh-pages manifest). A failure there reports `unknown`. They were left out on 2026-10-27 because `docs/dev/releasing.md` has no recovery steps for a half-finished release (for example a tag created but the release or manifest not published), so no banner could say what the safe next move is. Closes when `docs/dev/releasing.md` says how to recover from each release step failing and `gates.tsv` has a row per release step whose banner points there, or when the first real release failure adds them.

### Agent guide

#### AGENTS.md says the PR title is the changelog source, which a one-commit squash contradicts `[fid: agents-md-changelog-source-line]` `[captured 2026-10-27]`

`AGENTS.md` ("Commits & PR titles") says "The PR title is the changelog source for squash/merge PRs". The repo's squash setting is `COMMIT_OR_PR_TITLE` (read 2026-10-27 with `gh api repos/jellyrock/jellyfin-plugin-jellyrock`), so on a one-commit squash GitHub uses the commit's subject instead, and `scripts/changelog-gen.sh` reads first-parent subjects. Nothing breaks today because Commit Style lints both the title and the pushed subjects, but an agent following the line may fix the title and miss a wrong commit subject. Closes when the line says which subject reaches `CHANGELOG.md` in each merge mode (squash with one commit, squash with several, merge commit, rebase), matching `.github/pull_request_template.md`'s hint, or when the squash setting is changed to `PR_TITLE`.

## Batch jobs

Nothing currently running. (Long-running ingest, restores, migrations, or builds land here while in flight so `/catchup` can surface them.)

## Recurring cadences

| Cadence | Last run | Next due | Notes |
|---|---|---|---|
