# Cursor

Live state that survives between Claude Code sessions: in-flight non-project work, open followups, background jobs currently running, and the recurring-attention cadences. This is the "where am I, what's queued, what's due?" surface `/catchup` reads first.

Tracked projects are kept outside this repo, on the maintainer's machine (`docs/projects/` is gitignored); they have their own PLANs, statuses, and lifecycle skills (`/start-project` / `/resume-project` / `/end-session`). This cursor doc is for everything else: ad-hoc work between projects, followups captured via `/log followup` for next session, and the cadences that aren't crontabbed but still need human attention.

**Last updated**: 2026-10-29

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

#### PR titles reach users as release notes, but nothing says how to write them for users `[fid: pr-titles-read-as-release-notes]` `[captured 2026-10-29]`

The changelog entry is the squash commit's subject (`scripts/changelog-gen.sh` reads `git log`), and it can't be changed once merged. For 0.4.0 the entries for #33 ("leave the group of a JellyRock that closed on an HTTPS server"), #34 ("clear playback when JellyRock closes, and only then") and #35 ("stop ending music and paused videos on HTTPS…") read unclearly to users. They had to be reworded by hand in the dated section on the release branch (commit 20bc267 on `release-0.4.0`). `AGENTS.md` ("Commits & PR titles") and the `/pr` skill's title step say to write the title as the user-facing change, but give no test for it. Closes when both say that the title is the line a JellyRock user reads in the release notes and the plugin catalog: name the feature (SyncPlay, not "the group"), say what the user sees, and avoid negatives such as "stop ending". Related: `agents-md-changelog-source-line` covers the same section's wording about which subject reaches the changelog.

### Docs

#### Reword the Markdown voice hits the baseline still allows `[fid: voice-baseline-hits-to-zero]` `[captured 2026-10-27]`

`.voice-baseline.tsv` allows the voice hits the Markdown had when it was seeded: em dashes, arrows in prose and filler words in `AGENTS.md`, `docs/dev/contributing.md`, `docs/dev/releasing.md`, `README.md`, `docs/features/playback-cleanup.md` and `docs/features/remote-control.md`. Reword them, then run `bash .claude/skills/log/voice-check.sh --lower` so the baseline keeps the gain.

`bash .claude/skills/log/voice-check.sh --counts $(git ls-files '*.md' | grep -v CHANGELOG.md) | awk -F'\t' '$3 > 0'` lists what is left. Closes when that prints nothing and the baseline holds only its header.

### Remote control

#### The reaper and the long-poll controller read the clock directly, so their tests can't pin exact times `[fid: reaper-clock-timeprovider]` `[captured 2026-10-29]`

`PlaybackReaperService.SweepAsync()` and `QueueingSessionController` (`IsPollFresh()`, `MarkPolled()`, `MarkPollEnded()`) read `DateTime.UtcNow` directly. So the reaper's sweep tests assert position ranges (for example 598 to 600 s) instead of exact ticks, and a test can only make a poll window lapse by building a zero-length one. Inject `TimeProvider` (in the BCL since .NET 8; the plugin targets `net9.0`) into both, with `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in the tests. Found in the PR #34 review on 2026-10-29; left out of that PR because it changes the long-poll controller, which the fix does not touch. Closes when neither class reads `DateTime.UtcNow` and the sweep tests assert exact ticks.

## Batch jobs

Nothing currently running. (Long-running ingest, restores, migrations, or builds land here while in flight so `/catchup` can surface them.)

## Recurring cadences

| Cadence | Last run | Next due | Notes |
|---|---|---|---|
