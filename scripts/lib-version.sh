#!/usr/bin/env bash
# Shared helpers for reading/writing the plugin version.
#
# The version lives in exactly two files and MUST stay in lockstep (review
# finding #6). This library is the single place that knows how to read and
# write each one, so set-version.sh and check-version-parity.sh can't drift.
#
#   Directory.Build.props   <Version> / <AssemblyVersion> / <FileVersion>  (assembly + local/CI build)
#   Jellyfin.Plugin.JellyRock/build.yaml   version:   (jprm package + published manifest)
#
# Canonical on-disk form is 4-part "x.y.z.0" (C# assembly version). Humans and
# git tags use 3-part "x.y.z" / "vx.y.z".
set -euo pipefail

repo_root() { git -C "${BASH_SOURCE%/*}" rev-parse --show-toplevel; }

PROPS_FILE_REL="Directory.Build.props"
BUILD_YAML_REL="Jellyfin.Plugin.JellyRock/build.yaml"

# Read the 4-part version (e.g. 0.1.0.0) from Directory.Build.props <Version>.
read_props_version() {
  local root; root="$(repo_root)"
  grep -oP '(?<=<Version>)[^<]+' "$root/$PROPS_FILE_REL" | head -n1
}

# Read the 4-part version from build.yaml's `version:` line.
read_yaml_version() {
  local root; root="$(repo_root)"
  # Matches: version: "0.1.0.0"  (quotes optional, surrounding whitespace tolerated)
  grep -oP '^version:\s*"?\K[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' "$root/$BUILD_YAML_REL" | head -n1
}
