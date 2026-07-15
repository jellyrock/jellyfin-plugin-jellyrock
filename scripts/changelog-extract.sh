#!/usr/bin/env bash
# Print the CHANGELOG.md body for one version — the text between its
# `## [x.y.z]` heading and the next `## ` heading, with surrounding blank
# lines trimmed. Used for both the GitHub release notes and the manifest
# changelog, so CHANGELOG.md is the single source of release-note truth.
#
# Usage: changelog-extract.sh <x.y.z>
#        changelog-extract.sh <x.y.z> --oneline   # collapse to a single line
set -euo pipefail

version="${1:?usage: changelog-extract.sh <x.y.z> [--oneline]}"
oneline="${2:-}"

root="$(git -C "${BASH_SOURCE%/*}" rev-parse --show-toplevel)"
changelog="$root/CHANGELOG.md"

# awk: start printing after the "## [<version>]" heading, stop at the next "## ".
body="$(awk -v ver="$version" '
  $0 ~ "^## \\[" ver "\\]" { grab=1; next }
  grab && /^## / { exit }
  grab { print }
' "$changelog")"

# Trim leading/trailing blank lines.
body="$(printf '%s\n' "$body" | sed -e '/./,$!d' | tac | sed -e '/./,$!d' | tac)"

if [[ -z "$body" ]]; then
  echo "::error::No CHANGELOG.md section found for version $version" >&2
  exit 1
fi

if [[ "$oneline" == "--oneline" ]]; then
  # Flatten to one line for the manifest changelog (shown in the Dashboard).
  # Reflow by bullet: a "- "/"* " line starts an item; hard-wrapped
  # continuation lines fold back into it. Drop "###" sub-headings. Join the
  # resulting items with "; ".
  printf '%s\n' "$body" | awk '
    /^###? / { next }                                  # skip sub-headings
    /^[-*] / {                                          # new bullet
      if (item != "") print item
      sub(/^[-*] +/, ""); item = $0; next
    }
    /^[[:space:]]*$/ { next }                           # blank line
    {                                                   # continuation of a bullet
      sub(/^[[:space:]]+/, "")
      item = (item == "" ? $0 : item " " $0)
    }
    END { if (item != "") print item }
  ' | sed ':a;N;$!ba;s/\n/; /g'
else
  printf '%s\n' "$body"
fi
