#!/usr/bin/env bash
# CI backstop for review finding #6: assert the assembly version in
# Directory.Build.props and the manifest version in build.yaml are identical.
# set-version.sh always writes both, so a mismatch means someone hand-edited
# one file — fail the build before it can ship a manifest that disagrees with
# the DLL it points at.
set -euo pipefail
source "${BASH_SOURCE%/*}/lib-version.sh"

props="$(read_props_version || true)"
yaml="$(read_yaml_version || true)"

if [[ -z "$props" ]]; then
  echo "::error::Could not read <Version> from $PROPS_FILE_REL" >&2
  exit 1
fi
if [[ -z "$yaml" ]]; then
  echo "::error::Could not read version: from $BUILD_YAML_REL" >&2
  exit 1
fi

if [[ "$props" != "$yaml" ]]; then
  echo "::error::Version mismatch — $PROPS_FILE_REL has '$props' but $BUILD_YAML_REL has '$yaml'." >&2
  echo "Run scripts/set-version.sh <x.y.z> to rewrite both in lockstep." >&2
  exit 1
fi

echo "✅ Version parity OK: $props ($PROPS_FILE_REL == $BUILD_YAML_REL)"
