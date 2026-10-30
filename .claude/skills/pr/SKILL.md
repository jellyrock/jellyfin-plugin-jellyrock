---
name: pr
model: sonnet
effort: low
description: "The one path for creating or updating a pull request in this repo; it supersedes any default PR-creation flow. The title is a conventional commit that `scripts/lint-commit-subject.sh` checks, and its type places the change in CHANGELOG.md. The body fills `.github/pull_request_template.md` for the reviewer, since a squash here keeps the commit messages, not the body. Each deliverable gets one label from the shared set (`new-feature`, `bug-fix`, `dev-improvement` and the rest), never Renovate's. An open PR for the branch takes the update path (diff, confirm, back up, then edit); a merged or closed one aborts cleanly. The decision and followup passes run through /log before posting, so journal hygiene lands in the same change set."
---

# Create or Update a Pull Request

## This repo

- **Merge settings fallback** (Step 3): squash, merge commit and rebase are all allowed. Squash uses `COMMIT_OR_PR_TITLE` and `COMMIT_MESSAGES`, so the body never reaches `git log`: write it for the reviewer.
- **Title gate and types** (Steps 5, 6, 9): `scripts/lint-commit-subject.sh "<title>"` must pass; it prints the types. `scripts/changelog-gen.sh` sorts them: `feat` is Added, `fix` is Fixed, `perf`, `refactor` and `revert` are Changed, and the rest are left out. A change to the plugin (`Jellyfin.Plugin.JellyRock/`) takes a product type. A change only to tooling (`scripts/`, `tools/`, `.github/`, `.claude/`, `docs/dev/`, `Jellyfin.Plugin.JellyRock.Tests/`, `renovate.json`, `global.json`) takes `chore`, `ci`, `build`, `test` or `docs`. On a one-commit squash GitHub uses the commit's subject instead of the title, so check that it passes the gate and says the same thing.
- **PR checks** (Step 9): the test project in the SDK container ([docs/dev/README.md](../../../docs/dev/README.md#test)). Also `scripts/check-server-abi.sh` when the change touches which server types or members the plugin uses.
- **Sections** (Step 8): **Overview**, **Changes** and **Testing** are required. **Testing** says "Not tested" and why when nothing ran. **Issues** is optional, and links the JellyRock app PR when the change needs a matching app release.
- **Labels** (Step 7): none beyond the shared table. Renovate owns `dependencies`, `major`, `minor`, `patch`, `digest`, `pin` and `lockfile`: never touch them.

## Contract

**Goal.** Be the single, mandatory path for opening or updating a pull request in this repo. The skill reads what a squash merge keeps (the repo's merge settings, read live), titles the PR for the place its title ends up, renders the body from the repo's PR template for whoever reads it (the permanent commit message, or a reviewer only), labels each deliverable, and runs the journal passes (decision and followup, plus any this repo adds) before anything is posted, so journal hygiene lands in the same change set rather than as separate manual chores. It is create-or-update aware: an existing open PR for the branch routes to update-mode (diff the current title, labels, body and notes against a fresh render, confirm, back up, then apply), and merged or closed PRs abort cleanly instead of opening a duplicate. It ships at the Sonnet tier because the work is template-fill and structured signal-gathering with bounded judgment; a pass that walks a large ledger runs as its own sub-agent with an explicit model. Supersede any default PR-creation flow with this skill; never open or edit a pull request outside it, through `forge.sh` or the forge directly.

**Inputs.** No arguments: the skill operates on the current branch and its commits. It expects a non-default, non-detached branch with a clean working tree; the pre-flight pushes the branch and any unpushed commits. It reads the repo's PR template, its squash-merge settings, the branch's commit log and diff against the default branch, an existing PR's title, labels, body and notes comment (when one is open) including the render marker, and this skill's `## This repo` for the repo's title rules, labels, own journal passes, PR checks and required sections.

**Outputs.** A created or updated pull request with: a title that passes the repo's title gate, names every user-visible change and backticks code identifiers; one label per kind of deliverable; a body that is the filled template, written for the reader the merge settings give it, listing each message or work request part from another repo that the branch handles; when the body becomes the commit message, one notes comment for reviewer-only detail; and a hidden render marker (a full `sha` and an ISO-8601 UTC timestamp) that lets the next run narrow the journal passes to "since last render". Also: drafted journal entries surfaced per candidate for the user to accept into `/log`; on the update path, a backup of the prior body and notes inside the git directory; and the PR URL printed. No journal entry is written without a per-candidate accept, and nothing on the PR is overwritten without confirmation.

**Success criteria.**

- The pre-flight gates hold: not on the default branch, not detached, clean tree, branch and commits pushed. Hard failures stop and report; an obvious push is not gated behind a verbal question.
- Existing-PR routing is correct: an open PR takes the update path (diff, confirm, back up, apply); a merged or closed one aborts with the right recovery instruction; none takes the create path, and a merged or closed PR that only shares the branch's name counts as none.
- What reaches `git log` is read from the repo's settings, not assumed: the body is written as a commit message only when the merge keeps it, and the title gets the most care where the merge keeps it.
- The journal passes run against the resolved lower-bound commit (the prior render marker on update, the default branch on create), so the user isn't re-asked about candidates already handled, and each candidate is confirm/skip per item.
- The title passes the repo's title gate, backticks code identifiers and names every user-visible change; on the update path it is re-derived from the whole PR.
- Labels come from the deliverables the title and the body's summary name, each shown to the user beside the phrase that earned it; tooling-only work is `dev-improvement`, `documentation` goes only on a PR that changes nothing but docs, and labels this skill does not manage are never removed.
- The body passes the repo's own PR checks before it is posted, every issue reference points where its sentence means, and in a public repo nothing private is posted and the agent files appear only as a bare file list.
- `forge.sh pr create`, `pr edit` and `pr comment` permission prompts are left intact: they are the user's gate on what gets posted, not friction to allowlist away.

**Failure modes to avoid.**

- **Writing for the wrong reader.** A body written as a reviewer's note when the merge makes it the permanent commit message, or a commit-message body on a repo whose merge discards it. Read the settings first.
- **Polluting the skill's context with a ledger walk.** A pass that reads a large ledger runs as a sub-agent with an explicit model; reading the ledger inline is the context pollution the sub-agent exists to prevent, and narrating a sub-agent while the transcript shows an inline read is a false report.
- **Auto-applying journal entries.** The passes produce drafts only; invoke `/log` (or apply a ledger edit) only on an explicit per-candidate accept.
- **Overwriting a PR without confirmation or backup.** On the update path, always diff, confirm and back up the prior body and notes before `forge.sh pr edit`; if an edit fails, the backup is the recovery path: surface it, don't claim success.
- **Opening a duplicate on a merged or closed PR.** Abort with the recovery instruction; never silently create a second PR.
- **A title that names only part of the PR.** Where the title becomes the commit subject or a changelog line it cannot be fixed after merge; re-derive it from every user-visible change, on the update path too. The likeliest omission is a behavioral fix delivered inside a refactor.
- **A type picked by habit.** Where the title's type places the change (a changelog section, a release note), choose it from what the PR delivers: a tooling-only change given a user-facing type lands in the users' notes, and a product change given a housekeeping type drops out of them.
- **Labeling a PR's ingredients instead of its purposes.** A helper the fix needed, or the tests and docs that prove or explain it, earn no label of their own; a label with no title or summary phrase to point to is dropped.
- **Detail stuffed into a commit-message body.** Measurement tables, device or host matrices and reviewer asides belong in the notes comment, with a one-line summary of the evidence left in the body.
- **Dropping a code reference to pass a check.** When a title or spelling check rejects an identifier, backtick it; rephrasing the reference away is the wrong fix.
- **Silencing `forge.sh`, or reaching past it.** `2>/dev/null` turns the forge's exact error into a guess; each verb's fields are fixed, so a field it lacks is a change to `forge.sh`, never a direct forge call.
- **Creating a label to fit the PR.** A new label for each PR fragments the repo's label set; a missing label is replaced by the closest existing one first, and `forge.sh label create` is proposed only as the last option, never run silently.
- **Suppressing the create, edit or comment permission prompts** by allowlisting them: they are intentional user gates.

**When not to use.**

- There is no branch to ship (on the default branch, or nothing committed): there's nothing to open a PR for.
- You need to bypass the journal passes for a trivial change: that is still in scope (skip the passes with one confirmation), not a reason to call `forge.sh pr create` directly.

## Implementation

This skill's `## This repo` holds the repo's own parts: the title gate and its types, the product paths that separate product work from tooling, labels beyond the shared table and the labels automation owns, any journal pass of its own, the PR checks to run before posting, guidance for each template section, what to say after posting, and the merge-settings fallback line. Where a step needs one of them, it says so.

### Step 1: Pre-flight (abort if any fails)

Run in parallel:

- `git rev-parse --abbrev-ref HEAD`: must not be the default branch (`bash .claude/skills/pr/forge.sh repo default-branch`, called `{{default}}` below) and must not be `HEAD` (detached: ask the user to check out a branch).
- `git status --porcelain`: must be empty.
- `git rev-parse --abbrev-ref --symbolic-full-name @{u}`: with no upstream, run `git push -u origin HEAD`.
- `git rev-list --count @{u}..HEAD`: when it is not zero, run `git push`.

A failed hard check (default branch, detached, dirty tree) stops with which one and why. Pushing a feature branch to open its PR is obvious: don't ask first.

### Step 2: Detect an existing PR (route create vs update)

`bash .claude/skills/pr/forge.sh pr view` for the current branch: JSON with `number`, `url`, `state`, `author`, `title`, `labels`, `body` and `headRefOid`. Never add `2>/dev/null`: forge.sh passes the forge's own message on whole.

- **`MERGED`**: abort. `PR #{{N}} is already merged at {{url}}. Switch to {{default}} and pull before opening a follow-up PR.`
- **`CLOSED`**: abort. `PR #{{N}} at {{url}} was closed without merging. Reopen it with bash .claude/skills/pr/forge.sh pr reopen {{N}}, or start a new branch.`
- **`OPEN`**: the update path. Keep `{{N}}`, `{{url}}`, `{{author}}`, `{{title}}`, `{{labels}}`, `{{body}}`.
- **No PR** (`forge.sh pr view` exits 4; any other non-zero exit is an error: show its message and stop): the create path; `{{lower}}` is `{{default}}`. When its message names an earlier PR that only shares the branch name, first print one line: `Note: this branch reuses the name of PR #{{N}} ({{state}}), which none of its commits are in; opening a new PR.`

On the update path:

1. **Author note.** `bash .claude/skills/pr/forge.sh user login`; when it differs from `{{author}}`, print one line: `Note: PR #{{N}} was opened by {{author}}. Edits will appear under your account.` If the call fails, skip the note.
2. **Find the notes comment.** `bash .claude/skills/pr/forge.sh pr comments {{N}} | jq -c '.comments[] | select(.body | startswith("<!-- /pr notes -->")) | {url, author: .author.login, body}'`; keep the last match authored by you as `{{notes}}`. Its numeric id is the `#issuecomment-{{id}}` suffix of its `url` (the `id` field is a GraphQL node id the REST call cannot use).
3. **Resolve `{{lower}}`**, the lower bound for the journal passes:
   1. The last `<!-- /pr render: sha=([0-9a-f]{40}) ts=(\S+) -->` in `{{notes}}`, else in `{{body}}`; use its sha when `git merge-base --is-ancestor {{sha}} HEAD` exits 0.
   2. Else the PR's first commit, `bash .claude/skills/pr/forge.sh pr commits {{N}} | jq -r '.commits[0].oid'`, when it is an ancestor of `HEAD`.
   3. Else `{{default}}`, and print: `Note: the last render's commit is not in this branch (rebase or force-push?); the journal passes cover the whole branch.`

### Step 3: Read what the merge keeps

`bash .claude/skills/pr/forge.sh repo merge-settings`: JSON with `squash_merge_commit_title` and `squash_merge_commit_message`. These come back only with enough rights on the repo; when they are null or empty, use the merge-settings line in this skill's `## This repo`, and when that is missing too, ask in chat which of the readings below holds. The result decides two things:

- **The title.** `PR_TITLE`: the title becomes the commit subject on `{{default}}`. `COMMIT_OR_PR_TITLE`: it does on a multi-commit PR, but a single-commit PR keeps the commit's own subject, so on a one-commit branch the care goes into that subject (the title is still gated). Those are the only two values the API defines.
- **The body.** `PR_BODY`: the body is the permanent commit message, so it is written for someone reading `git log` a year from now, and reviewer-only detail goes in the notes comment (Step 8), which also carries the render marker. `COMMIT_MESSAGES` or `BLANK` (the API's other two values): the body never reaches `git log`, so it is written for a reviewer, holds the detail itself, and ends with the render marker; no notes comment.

### Step 4: Journal passes (before drafting the body)

Each pass surfaces journal entries the user should write, one-line confirm/skip per candidate; drafts only, applied through `/log` on accept. One confirmation skips the whole step on a trivial change (a typo, a dependency bump, docs only). Every pass covers `{{lower}}..HEAD`, so candidates handled on an earlier render are not raised again.

- **Decision:** commits that close off alternatives or carry a non-obvious rationale (architectural, hard to reverse, cross-component). On accept, `/log decision`, which applies its own significance gate.
- **Followup:** anything the branch defers, half-does or leaves for later, including what you are about to write as a follow-up in the body. On accept, `/log followup`, and the body cites the entry by its fid or title, never by its position in a list.
- **This repo's own passes**, from this skill's `## This repo`, only for [capture types](../../../AGENTS.md#capture-types) or ledgers this repo keeps. A pass that reads a large ledger runs as a sub-agent with an explicit `model`, never as an inline read.

### Step 5: Gather context (in parallel)

- `git log {{default}}..HEAD --pretty=format:"%h %s%n%b%n---"`: the full commit history on the branch.
- `git diff {{default}}...HEAD --stat` and `--name-only`: what changed.
- The PR template (`.github/pull_request_template.md`, or the path this skill's `## This repo` gives): read it, hints included.
- Anything this skill's `## This repo` lists for this step (a title-type definition, a related-docs check).

### Step 6: Title

**Agent files in a public repo.** When [this repo's public posture](../../../AGENTS.md#public-posture) is `public` and `git diff --name-status {{default}}...HEAD | bash .claude/skills/pr/agent-body.sh kind` prints `agent-only`, the title is exactly what `bash .claude/skills/pr/agent-body.sh title` prints, and the rest of this step does not apply. Each commit on such a branch has that title as its subject and no body.

`type: Imperative summary` or `type(scope): Imperative summary`, under 70 characters, passed with `--title`; the types and the gate that checks them are in this skill's `## This repo`. Synthesize it from all the commits, not the latest. Pick the type by what the PR does to the product (this skill's `## This repo` names the product paths): a change to the product takes a product type; a change only to the tooling around it takes a tooling type, even when it fixes something; a PR with both is typed by its product change. Scope is optional; never write an empty one.

**Name every user-visible change.** List the changes a user of the product would notice, then check the title names each; when they don't fit, name the outcome that covers them all rather than the biggest alone. A refactor that changes behavior is not a refactor here: if you can say "X used to sometimes fail, now it doesn't", it is user-visible, and a commit subject that joins a fix to a restructure with "and" needs each half checked. On the update path, re-derive the title from the whole PR; a PR that grew in review is when a title goes stale.

**Backtick every code identifier** (a component, file or function name). When a check rejects it, keep the backticks; never rephrase the reference away.

### Step 7: Labels

Labels are for finding PRs in the forge's list: a label is right when someone filtering by it would want this PR. Pick them after the body is final:

1. **Find the deliverables.** Read the title and the body's summary section. A deliverable is something they present as what the PR **does**; something presented only as how or why another deliverable works is part of that one. A change that appears only in the list of changes (a refactor the fix needed, the tests or docs that prove or explain a deliverable) earns no label.
2. **Label each deliverable** with the one best label below, or one this skill's `## This repo` adds. A PR with different kinds of deliverable gets several labels.
3. **Tooling is not the product.** `new-feature`, `general-improvement`, `bug-fix` and `code-cleanup` describe the product (the paths in this skill's `## This repo`). A deliverable that changes only tooling is `dev-improvement`, whether it fixes, adds or tidies something; a PR that changes only tooling is `dev-improvement` alone.
4. **Name the evidence.** Show the user each label beside the title or summary phrase that earned it, before create or edit, so it can be overruled in one reply. A label with no phrase to point to is dropped.

| Label | The deliverable… |
|---|---|
| `new-feature` | adds a capability the product's users can use |
| `general-improvement` | makes behavior that was working better on purpose: UX, speed, wording, robustness |
| `bug-fix` | makes the product do what it should have done already |
| `code-cleanup` | restructures or tidies product code as a goal of the PR, meant to change nothing a user sees |
| `dev-improvement` | changes only tooling (rule 3) |
| `documentation` | the PR changes **only** docs; never on a PR that also changes code |

Check each chosen label exists (`bash .claude/skills/pr/forge.sh label list`: every label with its description). For a missing one, in this order: use the existing label whose name and description fit the same deliverable, shown beside the label it replaces; else leave it off and say so; and only when the user wants it and nothing existing fits, show the `bash .claude/skills/pr/forge.sh label create {{name}} --description "{{text}}"` command for them to run or approve. Never create a label silently. Leave alone the labels automation owns (this skill's `## This repo` lists them) and any label not in the table.

### Step 8: Build the body (and the notes comment)

**Agent files in a public repo.** When [this repo's public posture](../../../AGENTS.md#public-posture) is `public`, run `git diff --name-status {{default}}...HEAD | bash .claude/skills/pr/agent-body.sh kind` first. `agent-only`: the body is exactly what `agent-body.sh body` prints from the same input (no template, no handled lines), followed by the render marker when the body is not the commit message; a notes comment holds only its first line and the marker. `mixed`: write the body below about the `other` files of `agent-body.sh split` alone, and end its changes section with what `agent-body.sh body` prints; neither the body nor the notes say anything else about the agent files. `none`: the steps below as they are.

Start from the template. Fill each section the way its hint comment asks (a section the hint marks required is never left empty; an optional one is left out or marked `None` as that hint says), then remove the hint comments. Keep the headings you use exactly as the template spells them. This skill's `## This repo` has the repo's guidance per section. Fill checkboxes from what the diff and Step 4 actually produced, never from intent.

- **Links are absolute URLs** to the default branch (get each from `bash .claude/skills/pr/forge.sh repo file-url {{path}}`, which knows the repo's forge): a repo-relative link is emitted as written and 404s from the PR page. Link the default branch, not the PR branch (deleted on merge); a file the PR adds is named in backticks instead.
- **A bare `#N` is always this repo's issue N**, whatever repo the sentence names. Write another repo's issue or PR as `owner/repo#N` or a full URL. They usually arrive by copying a commit message: rewrite them on the way in.
- **Related issues.** First the branch name (a number in it) and the commit messages (`(?i)(fix|fixes|close|closes|resolve|resolves|ref|refs|see)\s*#(\d+)`), each confirmed with `bash .claude/skills/pr/forge.sh issue view {{N}} | jq -c '{number, title, state}'`. Only when that finds nothing: 2–4 keywords from the title, `bash .claude/skills/pr/forge.sh issue list --state open --search "{{keywords}}" --limit 10`, read as candidates, not answers. Render `Fixes #N` when the PR closes it, `Ref #N` when only related; when several candidates are plausible and you can't judge between them, list them and ask.
- **What it handles.** `bash .claude/skills/start-project/mailbox.sh handled {{branch}}` prints a line for each message or work request part from another repo that `/done` marked handled on this branch. Put them in the body as printed, after the related issues, so the merge's commit records them; nothing printed means nothing to add. A `NOTE:` line names one marked with a commit no longer on the branch (amended or rebased): tell the user, since it cannot close until `/done` marks it again with the commit now on the branch.

**When the body is the commit message (Step 3):** keep it to what a `git log` reader needs (the problem and the change, the symbols a reader would grep for, a one-line summary of the evidence), and put the rest in one notes comment:

```markdown
<!-- /pr notes -->
## Review notes

**Docs and journals:** {{what this PR updated — docs, decision records, journal entries — or "none", from Step 5's list of changed files, not intent.}}

{{Verification detail: matrices, measurements, before/after output, the reasoning behind a non-obvious call. Omit a part with nothing to say. Absolute URLs only.}}

<!-- /pr render: sha={{full-40-char-HEAD-sha}} ts={{ISO-8601-UTC}} -->
```

`<!-- /pr notes -->` stays the first line: it is how the update path finds the comment. The render marker goes in this notes comment only, never in the body, which becomes the commit message and would carry it into `git log`. **When the body is not the commit message** (Step 3), the body holds the detail and ends with the render marker line, and there is no notes comment. Either way, fill the marker from `git rev-parse HEAD` and `date -u +%Y-%m-%dT%H:%M:%SZ`, never by hand.

### Step 9: Check before posting

1. **This repo's PR checks**, from this skill's `## This repo` (a title gate, a body linter), on the rendered title, body and notes: each must pass; fix what it reports and run it again.
2. **Issue references.** Read every bare `#N` in the body and notes against the sentence it sits in, with `bash .claude/skills/pr/forge.sh issue view {{N}} | jq -c '{title, state}'` (an issue or a PR): a title that doesn't match the sentence means it belongs to another repo and becomes `owner/repo#N`. A reference you could not resolve is not a pass: check it by hand.
3. **Public repo.** When [this repo's public posture](../../../AGENTS.md#public-posture) is `public`, sweep the title, body, notes and the branch diff for a home path, a person's email, a secret or another repository's name: `git diff {{default}}...HEAD | grep -nEi '/home/|/Users/|@[a-z0-9.-]+\.[a-z]{2,}|BEGIN [A-Z ]*PRIVATE KEY'` for the diff, and a read for the text you wrote. Read each hit; remove what is private, and stop to ask about anything in the diff itself.
4. **The render marker's place.** When the body is the commit message (Step 3), `grep -c '/pr render' {{body-file}}` must print `0`; otherwise move the marker into the notes comment and run it again.

### Step 10: Create or update

**Create path:**

```sh
bash .claude/skills/pr/forge.sh pr create --base {{default}} --title "{{title}}" --label "{{label}}" [--label "{{label}}" …] --body-file {{body-file}}
bash .claude/skills/pr/forge.sh pr comment {{N}} --body-file {{notes-file}}    # only when Step 3 calls for a notes comment
```

Default to non-draft; use `--draft` only when the work is incomplete and you want CI early, and say so.

**Update path:**

1. **Render** the title, labels, body and notes the same way; the body describes the whole PR (`{{default}}..HEAD`), not the delta since the last render.
2. **Compare** each with what the PR has now. When everything is unchanged (the marker's timestamp ignored), print `PR #{{N}} already up to date at {{url}}` and stop: no backup, no edit. Otherwise show a diff for each part that changed (title, labels added and removed, the body section by section, the notes), and say which body sections a person likely edited by hand.
3. **Confirm** in chat: `apply`, `skip` (print `{{url}}` and stop) or `edit` (take the user's changes, then ask again).
4. **Back up** the prior body, then the prior notes if any, to `"$(git rev-parse --git-dir)/pr-backup-{{N}}-{{date -u +%Y%m%dT%H%M%SZ}}.md"`: inside the git directory it can never be committed. Outside auto mode Claude Code asks before any write there (a sensitive path), and a headless run refuses it: that approval is the last gate before the edit, so never skip the backup or move it elsewhere to avoid it.
5. **Apply** only the parts that changed: `bash .claude/skills/pr/forge.sh pr edit {{N}} [--title "{{title}}"] [--add-label "{{a}},{{b}}"] [--remove-label "{{c}}"] [--body-file {{body-file}}]` (remove only a label from Step 7's table, or this repo's additions, that no longer applies); edit the notes comment by id with `bash .claude/skills/pr/forge.sh comment edit {{id}} --body-file {{notes-file}}`, or create it with `forge.sh pr comment` when there was none. If an edit fails, give the backup path and stop; the body is restored with `bash .claude/skills/pr/forge.sh pr edit {{N}} --body-file {{backup}}` after trimming the notes off its end.

### Step 11: After

Print the PR URL; don't summarize the body. Apply the accepted journal drafts through `/log`. Then anything this skill's `## This repo` asks for after posting.
