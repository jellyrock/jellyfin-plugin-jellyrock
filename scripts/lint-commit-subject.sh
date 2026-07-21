#!/usr/bin/env bash
# Validate one commit subject / PR title against the conventional-commit format the
# changelog generator (changelog-gen.sh) categorizes by. A non-conventional subject
# still produces a changelog entry — but silently defaults to "Changed", which is how
# #9's cold-launch feature first landed under Changed instead of Added. Requiring an
# explicit type makes the changelog category intentional, not defaulted.
#
# No toolchain: pure bash, runs the same in CI and locally.
#
#   Usage: scripts/lint-commit-subject.sh "<subject>"
#   Exit:  0 valid · 1 invalid (prints why)
set -euo pipefail

subject="${1:?usage: lint-commit-subject.sh \"<subject>\"}"

# Types MUST match changelog-gen.sh's mapping so the gate and the generator agree:
#   feat -> Added · fix -> Fixed · perf|refactor|revert -> Changed
#   chore|ci|build|test|style|docs -> skipped (housekeeping)
types='feat|fix|perf|refactor|revert|chore|ci|build|test|style|docs'
re="^(${types})(\([a-z0-9._/-]+\))?!?: .+"

if [[ "$subject" =~ $re ]]; then
  echo "✅ ${subject}"
  exit 0
fi

cat >&2 <<EOF
::error::Not a conventional commit subject / PR title:
    ${subject}

  Expected: <type>[(scope)][!]: <description>
  Types:    ${types//|/, }
  Examples: feat(cold-cast): advertise closed devices as Play On targets
            fix: stop the reaper from advancing the saved resume position
            docs: correct the cold-launch cast app-version floor

  Why: the changelog groups entries by type (feat -> Added, fix -> Fixed, ...).
  A subject with no recognized type silently lands under "Changed".
  See CONTRIBUTING.md.
EOF
exit 1
