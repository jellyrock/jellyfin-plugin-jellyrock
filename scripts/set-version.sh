#!/usr/bin/env bash
# Single source of truth for a version bump. Rewrites BOTH version files in
# lockstep (review finding #6) and rolls the CHANGELOG.md [Unreleased] section
# into a dated [x.y.z] section.
#
#   Usage: scripts/set-version.sh <x.y.z>
#
# Env:
#   RELEASE_DATE=YYYY-MM-DD   override the changelog date (default: UTC today)
#   FORCE=1                   allow an empty [Unreleased] section (skips the guard)
#
# Writes:
#   Directory.Build.props              <Version>/<AssemblyVersion>/<FileVersion> = x.y.z.0
#   Jellyfin.Plugin.JellyRock/build.yaml   version: "x.y.z.0"
#   CHANGELOG.md                       [Unreleased] -> [x.y.z] - DATE
set -euo pipefail
source "${BASH_SOURCE%/*}/lib-version.sh"

ver="${1:?usage: set-version.sh <x.y.z>}"
if [[ ! "$ver" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::Version must be 3-part semver x.y.z (got '$ver')" >&2
  exit 1
fi
four="${ver}.0"                         # 4-part assembly form
date="${RELEASE_DATE:-$(date -u +%F)}"
root="$(repo_root)"
props="$root/$PROPS_FILE_REL"
yaml="$root/$BUILD_YAML_REL"
changelog="$root/CHANGELOG.md"

# --- Directory.Build.props ---------------------------------------------------
sed -i -E \
  -e "s#<Version>[^<]*</Version>#<Version>${four}</Version>#" \
  -e "s#<AssemblyVersion>[^<]*</AssemblyVersion>#<AssemblyVersion>${four}</AssemblyVersion>#" \
  -e "s#<FileVersion>[^<]*</FileVersion>#<FileVersion>${four}</FileVersion>#" \
  "$props"

# --- build.yaml --------------------------------------------------------------
sed -i -E "s#^version:.*#version: \"${four}\"#" "$yaml"

# --- CHANGELOG.md ------------------------------------------------------------
# Guard: refuse to cut a release with an empty [Unreleased] (unless FORCE=1).
unreleased_body="$(awk '
  /^## \[Unreleased\]/ { grab=1; next }
  grab && /^## / { exit }
  grab { print }
' "$changelog" | sed -E 's/^###? .*//; s/^\s+//; s/\s+$//' | awk 'NF')"

if [[ -z "$unreleased_body" && "${FORCE:-}" != "1" ]]; then
  echo "::error::CHANGELOG.md [Unreleased] is empty — nothing to release." >&2
  echo "Add entries under ## [Unreleased], or set FORCE=1 to override." >&2
  exit 1
fi

# Insert a dated [x.y.z] heading immediately under [Unreleased]; the existing
# unreleased content thereby moves under the new version heading.
tmp="$(mktemp)"
awk -v ver="$ver" -v date="$date" '
  !done && /^## \[Unreleased\]/ {
    print "## [Unreleased]"
    print ""
    print "## [" ver "] - " date
    done=1
    next
  }
  { print }
' "$changelog" > "$tmp"
mv "$tmp" "$changelog"

echo "✅ Bumped to ${ver} (assembly ${four}, changelog dated ${date})"
echo "   - $PROPS_FILE_REL"
echo "   - $BUILD_YAML_REL"
echo "   - CHANGELOG.md"
