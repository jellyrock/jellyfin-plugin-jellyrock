#!/usr/bin/env bash
# Exit 0 if version $1 is STRICTLY greater than version $2 (semver ordering via
# `sort -V`, so 0.10.0 > 0.9.0), else exit 1. Equal versions are not "greater".
#
#   version-gt.sh 0.2.0 0.1.0   -> exit 0
#   version-gt.sh 0.1.0 0.1.0   -> exit 1
#   version-gt.sh 0.1.0 0.2.0   -> exit 1
set -euo pipefail
a="${1:?usage: version-gt.sh <a> <b>}"
b="${2:?usage: version-gt.sh <a> <b>}"
[[ "$a" == "$b" ]] && exit 1
[[ "$(printf '%s\n%s\n' "$a" "$b" | sort -V | tail -n1)" == "$a" ]]
