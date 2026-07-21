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
#   CHANGELOG.md                       [Unreleased] -> [x.y.z](compare-url) - DATE
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
# Generate the release section from PR/commit titles since the previous tag
# (scripts/changelog-gen.sh) rather than a hand-written [Unreleased] — entries then
# mirror the JellyRock app's changelog. This runs on the release branch BEFORE the
# "chore: prepare release" commit exists, so only merged feature PRs are in range.
# [Unreleased] stays as an empty placeholder for the next cycle.
section="$("${BASH_SOURCE%/*}/changelog-gen.sh")"

if [[ -z "$section" && "${FORCE:-}" != "1" ]]; then
  echo "::error::No user-facing commits since the last release — nothing to changelog." >&2
  echo "Merge at least one feat/fix/perf/refactor PR, or set FORCE=1 to override." >&2
  exit 1
fi

# Compare URL for the dated heading (Keep-a-Changelog convention): link this
# version to the diff against its predecessor tag, or — for the very first
# release — to the tag page. The v$ver tag doesn't exist yet (release.yml creates
# it on merge), so the predecessor is simply the highest tag that currently exists.
slug="$(git -C "$root" config --get remote.origin.url \
        | sed -E 's#^(git@github.com:|https?://github.com/)##; s#\.git$##')"
prev_tag=""
while IFS= read -r t; do
  [[ -n "$t" ]] || continue
  if "${BASH_SOURCE%/*}/version-gt.sh" "$ver" "${t#v}"; then prev_tag="$t"; break; fi
done < <(git -C "$root" tag --list 'v*.*.*' --sort=-v:refname)
if [[ -n "$prev_tag" ]]; then
  heading_url="https://github.com/${slug}/compare/${prev_tag}...v${ver}"
else
  heading_url="https://github.com/${slug}/releases/tag/v${ver}"
fi

# Insert '## [x.y.z](compare-url) - DATE' + the generated section directly under
# '## [Unreleased]', dropping any stale [Unreleased] body (it is auto-generated
# per-merge by changelog-sync-unreleased.sh now, never hand-written).
tmp="$(mktemp)"
CL_VER="$ver" CL_DATE="$date" CL_URL="$heading_url" CL_SECTION="$section" awk '
  skip && /^## \[/ { skip=0; print "" }   # previous release heading — resume, one blank before it
  skip { next }                           # drop the old [Unreleased] body in between
  !done && /^## \[Unreleased\]/ {
    print "## [Unreleased]"
    print ""
    print "## [" ENVIRON["CL_VER"] "](" ENVIRON["CL_URL"] ") - " ENVIRON["CL_DATE"]
    if (ENVIRON["CL_SECTION"] != "") {
      print ""
      printf "%s", ENVIRON["CL_SECTION"]
      print ""
    }
    done=1
    skip=1
    next
  }
  { print }
' "$changelog" > "$tmp"
mv "$tmp" "$changelog"

echo "✅ Bumped to ${ver} (assembly ${four}, changelog dated ${date})"
echo "   - $PROPS_FILE_REL"
echo "   - $BUILD_YAML_REL"
echo "   - CHANGELOG.md"
