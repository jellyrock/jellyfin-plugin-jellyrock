#!/usr/bin/env bash
# Generate a Keep-a-Changelog section body from commit subjects since the previous
# release tag — the same idea as the JellyRock app's changelog-syncer, in bash so
# this .NET repo needs no Node toolchain.
#
# Every first-parent commit on main is one changelog candidate, regardless of how
# it landed (parity with the reference — see the merge handling below):
#   - squash merge / direct commit -> the commit subject IS the PR/change title
#   - merge-commit PR ("Merge pull request #N from …") -> the subject is resolved
#     to the PR title via `gh` so it yields the same entry a squash would have
#   - other "Merge …" commits (branch/pull-through merges) -> dropped as noise
# Conventional-commit types map to sections and the type prefix is stripped:
#   feat                  -> Added
#   fix                   -> Fixed
#   perf|refactor|revert  -> Changed
#   chore|ci|build|test|style|docs -> skipped (housekeeping, not user-facing)
#   anything else         -> Changed  (never silently drop a real change)
# A trailing "(#123)" PR ref becomes a markdown link to the PR.
#
# Resolving merge-commit PRs needs `gh` authenticated (GH_TOKEN in CI); it is only
# invoked for "Merge pull request" subjects, so squash-only history never calls it.
#
# Prints the section body (### headings + bullets) to stdout; writes no files. Empty
# output means nothing user-facing landed since the tag (the caller decides what to do).
#
#   Usage: scripts/changelog-gen.sh [<sinceTag>]
#          <sinceTag> defaults to the highest existing vX.Y.Z tag.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
since="${1:-$(git -C "$root" tag --list 'v*.*.*' --sort=-v:refname | head -n1)}"
range="HEAD"
[[ -n "$since" ]] && range="${since}..HEAD"

# owner/repo slug for PR links, derived from the origin remote.
slug="$(git -C "$root" config --get remote.origin.url \
        | sed -E 's#^(git@github.com:|https?://github.com/)##; s#\.git$##')"

merge_pr_re='^Merge pull request #([0-9]+) from (.+)$'

added=""; changed=""; fixed=""
while IFS= read -r subject; do
  [[ -n "$subject" ]] || continue

  # Normalize merge commits so a merge-commit PR produces the same entry a squash
  # or direct commit would. A squash/direct subject already IS the title, so it
  # falls straight through this block untouched.
  if [[ "$subject" =~ $merge_pr_re ]]; then
    prnum="${BASH_REMATCH[1]}"; src="${BASH_REMATCH[2]}"
    # Release-prep merges (the release-x.y.z branch) aren't a user-facing change.
    case "$src" in */release-*|release-*) continue ;; esac
    # Resolve the PR title; categorize/link it exactly like a squash subject.
    title="$(gh pr view "$prnum" --repo "$slug" --json title --jq .title 2>/dev/null || true)"
    subject="${title:-Merged PR} (#${prnum})"
  elif [[ "$subject" == Merge\ * ]]; then
    continue   # "Merge branch '…'", "Merge remote-tracking …" — merge noise, not a change
  fi

  # Leading token, lowercased: the conventional-commit type (or the first word).
  type="$(printf '%s' "$subject" | sed -E 's/^([A-Za-z]+).*/\1/' | tr '[:upper:]' '[:lower:]')"
  case "$type" in
    chore|ci|build|test|style|docs) continue ;;
  esac
  # Strip a conventional prefix "type(scope)!:" if present; leave prefix-less titles as-is.
  text="$(printf '%s' "$subject" | sed -E 's/^[A-Za-z]+(\([^)]+\))?!?:[[:space:]]*//')"
  # Linkify a trailing "(#123)" PR reference ('|' delimiter: the slug contains '/').
  text="$(printf '%s' "$text" | sed -E "s|\(#([0-9]+)\)|([#\1](https://github.com/${slug}/pull/\1))|g")"
  case "$type" in
    feat) added+="- ${text}"$'\n' ;;
    fix)  fixed+="- ${text}"$'\n' ;;
    *)    changed+="- ${text}"$'\n' ;;
  esac
# --first-parent WITHOUT --no-merges: one entry per landed change (squash, direct,
# or merge commit), never descending into a merged branch's individual commits.
done < <(git -C "$root" log --first-parent --pretty=%s "$range")

first=1
emit() { # $1 heading, $2 body
  [[ -n "$2" ]] || return 0
  [[ $first == 1 ]] || printf '\n'
  first=0
  printf '### %s\n\n%s' "$1" "$2"
}
emit "Added"   "$added"
emit "Changed" "$changed"
emit "Fixed"   "$fixed"
