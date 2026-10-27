# Cursor

Live state that survives between Claude Code sessions: in-flight non-project work, open followups, background jobs currently running, and the recurring-attention cadences. This is the "where am I, what's queued, what's due?" surface `/catchup` reads first.

Tracked projects are kept outside this repo, on the maintainer's machine (`docs/projects/` is gitignored); they have their own PLANs, statuses, and lifecycle skills (`/start-project` / `/resume-project` / `/end-session`). This cursor doc is for everything else: ad-hoc work between projects, followups captured via `/log followup` for next session, and the cadences that aren't crontabbed but still need human attention.

**Last updated**: 2026-10-27

## In-flight non-project work

Nothing currently in flight outside the tracked projects.

## Open followups

> Captured via `/log followup`, closed via `/done`, both through `.claude/skills/log/journal.sh`. Group by category (one `### Category` heading per area). Each followup is a `####` heading ending in its `[fid: …]` and `[captured …]` markers, with its body below it; refer to one by its fid, never by its position. Keep each a self-contained "what + why + where" so a future session can act on it cold.

None open.

## Batch jobs

Nothing currently running. (Long-running ingest, restores, migrations, or builds land here while in flight so `/catchup` can surface them.)

## Recurring cadences

| Cadence | Last run | Next due | Notes |
|---|---|---|---|
