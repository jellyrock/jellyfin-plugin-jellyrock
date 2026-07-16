#!/usr/bin/env bash
# Generate a Keep-a-Changelog section body from commit subjects since the previous
# release tag — the same idea as the JellyRock app's changelog-syncer, in bash so
# this .NET repo needs no Node toolchain.
#
# PRs are squash-merged, so each first-parent commit subject on main IS the PR title.
# Conventional-commit types map to sections and the type prefix is stripped:
#   feat                  -> Added
#   fix                   -> Fixed
#   perf|refactor|revert  -> Changed
#   chore|ci|build|test|style|docs -> skipped (housekeeping, not user-facing)
#   anything else         -> Changed  (never silently drop a real change)
# A trailing "(#123)" PR ref becomes a markdown link to the PR.
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

added=""; changed=""; fixed=""
while IFS= read -r subject; do
  [[ -n "$subject" ]] || continue
  # Leading token, lowercased: the conventional-commit type (or the first word).
  type="$(printf '%s' "$subject" | sed -E 's/^([A-Za-z]+).*/\1/' | tr '[:upper:]' '[:lower:]')"
  case "$type" in
    chore|ci|build|test|style|docs|merge) continue ;;
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
done < <(git -C "$root" log --first-parent --no-merges --pretty=%s "$range")

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
