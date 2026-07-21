#!/usr/bin/env bash
# Regenerate CHANGELOG.md's [Unreleased] section from the PRs/commits merged since
# the newest RELEASED version. This is the per-merge counterpart to set-version.sh's
# release-time roll-up: together they mirror the JellyRock app's changelog-syncer
# (sync-unreleased + sync-release), in bash so this .NET repo needs no Node toolchain.
#
# The [Unreleased] body is fully DERIVED, never hand-written (same contract as the
# release roll-up), so this OVERWRITES whatever currently sits under [Unreleased].
# Writes only CHANGELOG.md; the caller (.github/workflows/changelog-sync.yml) decides
# whether to commit. Prints no other files. Idempotent: re-running is a no-op.
#
#   Usage: scripts/changelog-sync-unreleased.sh
set -euo pipefail

here="${BASH_SOURCE%/*}"
root="$(git -C "$here" rev-parse --show-toplevel)"
changelog="$root/CHANGELOG.md"

# Anchor the "unreleased" range on the newest DATED changelog section, not merely
# the highest git tag. During the release window — a release PR merged to main but
# not yet tagged by release.yml — the newest dated version has no tag: everything
# up to HEAD already lives in that dated section, so [Unreleased] must be EMPTY
# rather than re-list those commits. Once the tag exists, the range is the usual
# <latest release>..HEAD that changelog-gen.sh computes.
newest="$(grep -oE '^## \[[0-9]+\.[0-9]+\.[0-9]+\]' "$changelog" \
          | head -n1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' || true)"

if [[ -z "$newest" ]]; then
  # No dated section yet — everything since repo start is unreleased.
  section="$("$here/changelog-gen.sh")"
elif git -C "$root" rev-parse -q --verify "refs/tags/v$newest" >/dev/null; then
  section="$("$here/changelog-gen.sh" "v$newest")"
else
  # Newest release is dated but not yet tagged (release in flight) — nothing is
  # unreleased; its commits belong to that pending dated section.
  section=""
fi

# Rewrite ONLY the [Unreleased] body — the lines between '## [Unreleased]' and the
# next '## ' version heading. Every dated section below is left byte-for-byte intact.
tmp="$(mktemp)"
CL_SECTION="$section" awk '
  !done && /^## \[Unreleased\]/ {
    print "## [Unreleased]"
    # print (not printf) adds the trailing newline that $(...) strips off $section,
    # so the last bullet line is terminated; the blank line before the next heading
    # is emitted by the resume rule below (mirrors set-version.sh).
    if (ENVIRON["CL_SECTION"] != "") { print ""; print ENVIRON["CL_SECTION"] }
    done=1; skip=1; next
  }
  skip && /^## / { skip=0; print "" }   # next version heading — one blank line, then resume
  skip { next }                          # drop the stale [Unreleased] body in between
  { print }
' "$changelog" > "$tmp"
mv "$tmp" "$changelog"

if git -C "$root" diff --quiet -- CHANGELOG.md; then
  echo "ℹ️ [Unreleased] already up to date${newest:+ (since v$newest)}."
else
  echo "✅ Synced [Unreleased]${newest:+ from v$newest..HEAD}."
fi
