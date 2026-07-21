#!/usr/bin/env bash
# Validate the STRUCTURAL invariants that the changelog automation depends on —
# not the content. Fails loudly (non-zero, ::error:: for CI) so a bad hand-edit
# can't silently break set-version.sh's roll-up, changelog-sync-unreleased.sh, or
# release.yml's "is this version dated yet?" gate. No network, no gh, no toolchain.
#
#   Usage: scripts/changelog-validate.sh [<path-to-CHANGELOG.md>]
set -euo pipefail

root="$(git -C "${BASH_SOURCE%/*}" rev-parse --show-toplevel)"
changelog="${1:-$root/CHANGELOG.md}"

fail() { echo "::error::changelog-validate: $1" >&2; bad=1; }
bad=0

# 1. Must exist and be readable.
if [[ ! -f "$changelog" ]]; then
  echo "::error::changelog-validate: $changelog is missing" >&2
  exit 1
fi

# 2. Keep-a-Changelog header markers (release notes + tooling assume this shape).
grep -qE '^# Changelog' "$changelog"        || fail "missing '# Changelog' header"
grep -qi 'Keep a Changelog' "$changelog"    || fail "missing the 'Keep a Changelog' reference line"

# 3. Exactly one '## [Unreleased]' anchor. set-version.sh and the per-merge sync
#    both insert/rewrite relative to this line; zero or two breaks both.
unreleased_count="$(grep -cE '^## \[Unreleased\]' "$changelog" || true)"
if [[ "$unreleased_count" -ne 1 ]]; then
  fail "expected exactly one '## [Unreleased]' section, found $unreleased_count"
fi

# 4. Every version heading (any '## [...]' that isn't [Unreleased]) must be a
#    well-formed dated + linked heading: '## [x.y.z](url) - YYYY-MM-DD'. This is
#    exactly what changelog-extract.sh and release.yml's grep rely on.
while IFS= read -r line; do
  [[ "$line" == '## [Unreleased]' ]] && continue
  if [[ ! "$line" =~ ^##\ \[[0-9]+\.[0-9]+\.[0-9]+\]\(https?://[^\)]+\)\ -\ [0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
    fail "malformed version heading: '$line' (want '## [x.y.z](url) - YYYY-MM-DD')"
  fi
done < <(grep -E '^## \[' "$changelog")

if [[ "$bad" -ne 0 ]]; then
  echo "❌ changelog-validate: structural problems found (see errors above)." >&2
  exit 1
fi
echo "✅ changelog-validate: structure OK ($changelog)."
