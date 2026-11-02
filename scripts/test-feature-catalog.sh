#!/usr/bin/env bash
# Test scripts/feature-catalog.sh on a temp tree built here; no file of the real tree is read.
#
#   Usage: scripts/test-feature-catalog.sh
#
# Needs bash, jq, python3 and PyYAML (the YAML check uses SafeLoader, as jprm does). A missing
# python3 or PyYAML fails the run rather than skipping the check.
set -euo pipefail

gen="$(cd "${BASH_SOURCE%/*}" && pwd)/feature-catalog.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

if ! python3 -c 'import yaml' 2>/dev/null; then
  echo "FAIL: python3 with PyYAML is required" >&2
  exit 1
fi

fails=0
pass() { echo "ok   $1"; }
fail() { echo "FAIL $1" >&2; fails=$((fails + 1)); }

page() {  # <file> <title> <pitch> <since> <jellyrock> <http> <https>
  printf -- '---\ntitle: %s\npitch: %s\nsince: %s\njellyrock: %s\nhttp: %s\nhttps: %s\n---\n\n# Body\n' \
    "$2" "$3" "$4" "$5" "$6" "$7" > "$1"
}

fresh() {  # <dir>: a valid tree
  local r="$1"
  mkdir -p "$r/docs/features" "$r/Jellyfin.Plugin.JellyRock"
  page "$r/docs/features/alpha.md" "Alpha feature" "Does alpha." 0.1.0 2.23.0 not-needed needed
  page "$r/docs/features/beta.md" "Beta feature" "Does beta, with a | pipe." 0.2.0 2.24.0 needed needed
  page "$r/docs/features/gamma.md" "Gamma feature" "Does gamma." unreleased 3.3.0 not-needed needed
  page "$r/docs/features/delta.md" "Delta feature" "Does delta: quoted \"text\" and a #hash." 0.2.0 2.23.0 needed not-needed
  cat > "$r/CHANGELOG.md" <<'EOF'
# Changelog

## [Unreleased]

## [0.2.0](https://example.com/compare/v0.1.0...v0.2.0) - 2026-07-16

### Added

- Something

## [0.1.0](https://example.com/releases/tag/v0.1.0) - 2026-07-14
EOF
  cat > "$r/README.md" <<'EOF'
# Readme

Intro.

<!-- BEGIN generated features (scripts/feature-catalog.sh) -->
stale text
<!-- END generated features -->

Outro.
EOF
  cat > "$r/Jellyfin.Plugin.JellyRock/build.yaml" <<'EOF'
---
name: "Test"
version: "0.2.0.0"
targetAbi: "10.11.0.0"
overview: "Kept as is."
description: >
  Old folded text
  on two lines.
category: "General"
artifacts:
  - "Test.dll"
EOF
}

run() { "$gen" --root "$1" "${@:2}" 2>&1; }

expect_fail() {  # <name> <dir> <message fragment> [args...]
  local name="$1" dir="$2" frag="$3" outp
  shift 3
  if outp="$(run "$dir" "$@")"; then fail "$name: expected a failure"; return; fi
  if grep -qF -- "$frag" <<<"$outp"; then pass "$name"; else fail "$name: output lacks '$frag': $outp"; fi
}

# --- the valid tree ----------------------------------------------------------
t="$tmp/valid"; fresh "$t"
out="$(run "$t")" || { fail "valid tree: $out"; }
cat="$t/docs/feature-catalog.json"
[[ "$(jq -r '.features | map(.title) | join(",")' "$cat")" == "Alpha feature,Beta feature,Delta feature,Gamma feature" ]] \
  && pass "features are sorted by title" || fail "sort order: $(jq -c '[.features[].title]' "$cat")"
[[ "$(jq -r '.plugin.server' "$cat")" == "10.11.0" ]] && pass "plugin.server is three parts" || fail "plugin.server"
[[ "$(jq -r '.schema' "$cat")" == "1" ]] && pass "schema is 1" || fail "schema"
[[ "$(jq -c '.features[] | select(.slug == "beta") | [.sinceDate, .url]' "$cat")" \
  == '["2026-07-16","https://github.com/jellyrock/jellyfin-plugin-jellyrock/blob/main/docs/features/beta.md"]' ]] \
  && pass "sinceDate and url" || fail "sinceDate/url: $(jq -c '.features[]' "$cat")"
[[ "$(jq -c '.features[] | select(.slug == "gamma") | .sinceDate' "$cat")" == "null" ]] \
  && pass "unreleased sinceDate is null" || fail "unreleased sinceDate"
[[ "$(tail -c1 "$cat" | xxd -p)" == "0a" ]] && pass "catalog ends in a newline" || fail "trailing newline"

readme="$t/README.md"
grep -qF '| [Gamma feature (next release)](docs/features/gamma.md) | Does gamma. | Works without it | Plugin needed | 3.3.0 or newer |' "$readme" \
  && pass "README row: next release, cells" || fail "README gamma row: $(cat "$readme")"
grep -qF 'Does beta, with a \| pipe.' "$readme" && pass "README escapes a pipe" || fail "README pipe"
grep -qF 'stale text' "$readme" && fail "README block was not replaced" || pass "README block replaced"
grep -qF 'Outro.' "$readme" && grep -qF 'Intro.' "$readme" && pass "README text outside the markers stays" || fail "README outside text"

yaml="$t/Jellyfin.Plugin.JellyRock/build.yaml"
expected_desc="The server-side companion for the JellyRock Roku app. Alpha feature: Does alpha. Needed on HTTPS servers, with JellyRock 2.23.0 or newer. Beta feature: Does beta, with a | pipe. Needed on HTTP and HTTPS servers, with JellyRock 2.24.0 or newer. Delta feature: Does delta: quoted \"text\" and a #hash. Needed on HTTP servers, with JellyRock 2.23.0 or newer. Gamma feature: Does gamma. Needed on HTTPS servers, with JellyRock 3.3.0 or newer."
got="$(python3 -I -c 'import sys, yaml; print(yaml.load(open(sys.argv[1]), Loader=yaml.SafeLoader)["description"], end="")' "$yaml")"
[[ "$got" == "$expected_desc" ]] && pass "build.yaml description loads with SafeLoader and matches" || fail "description: $got"
python3 -I -c 'import sys, yaml; d = yaml.load(open(sys.argv[1]), Loader=yaml.SafeLoader); assert d["category"] == "General" and d["artifacts"] == ["Test.dll"] and d["overview"] == "Kept as is."' "$yaml" \
  && pass "other build.yaml keys are untouched" || fail "other build.yaml keys"

before="$(cat "$cat" "$readme" "$yaml")"
out="$(run "$t")" && [[ "$out" == *"Nothing to change"* ]] && [[ "$before" == "$(cat "$cat" "$readme" "$yaml")" ]] \
  && pass "a second run changes nothing" || fail "second run: $out"
run "$t" --check >/dev/null && pass "--check passes on a fresh tree" || fail "--check on a fresh tree"

# --- a stale output ----------------------------------------------------------
sed -i 's/^pitch: Does alpha\./pitch: Does alpha, edited./' "$t/docs/features/alpha.md"
if outp="$(run "$t" --check)"; then fail "--check passed after a pitch edit"; else
  grep -qF 'docs/feature-catalog.json is out of date' <<<"$outp" && grep -qF 'Run scripts/feature-catalog.sh' <<<"$outp" \
    && pass "--check names the stale outputs" || fail "--check message: $outp"
fi
[[ "$(jq -r '.features[0].pitch' "$cat")" == "Does alpha." ]] && pass "--check writes nothing" || fail "--check wrote"
expect_fail "--check with --release is a usage error" "$t" "cannot be used together" --check --release 0.2.0

# --- validation failures -----------------------------------------------------
bad() {  # <name> <fragment> <sed expression on alpha.md>
  local d="$tmp/bad-$1"; fresh "$d"; sed -i -E "$3" "$d/docs/features/alpha.md"
  expect_fail "validation: $1" "$d" "docs/features/alpha.md: $2"
}
bad not-key-value "frontmatter line is not 'key: value'" 's/^http: .*/http needed/'
bad unknown-field "unknown field 'extra'" 's/^https: .*/&\nextra: 1/'
bad missing-field "missing field 'https'" '/^https: /d'
bad http-value "http must be 'needed' or 'not-needed'" 's/^http: .*/http: maybe/'
bad https-value "https must be 'needed' or 'not-needed'" 's/^https: .*/https: yes/'
bad empty-title "empty title" 's/^title: .*/title: /'
bad long-pitch "pitch is 121 characters" "s/^pitch: .*/pitch: $(printf 'x%.0s' $(seq 121))/"
bad jellyrock-form "jellyrock must be x.y.z" 's/^jellyrock: .*/jellyrock: 2.23/'
bad since-form "since must be 'unreleased' or x.y.z" 's/^since: .*/since: soon/'
bad since-no-heading "since 0.9.0 has no dated heading" 's/^since: .*/since: 0.9.0/'
d="$tmp/bad-no-close"; fresh "$d"
awk '/^---$/ { n++; if (n == 2) next } { print }' "$d/docs/features/alpha.md" > "$d/a" && mv "$d/a" "$d/docs/features/alpha.md"
expect_fail "validation: no-close" "$d" "docs/features/alpha.md: the frontmatter block has no closing ---"

d="$tmp/two-errors"; fresh "$d"
sed -i -E 's/^http: .*/http: maybe/' "$d/docs/features/alpha.md"
sed -i -E 's/^jellyrock: .*/jellyrock: 2/' "$d/docs/features/beta.md"
outp="$(run "$d" || true)"
grep -qF 'alpha.md: http must' <<<"$outp" && grep -qF 'beta.md: jellyrock must' <<<"$outp" \
  && pass "every problem is listed before failing" || fail "two errors: $outp"

d="$tmp/changelog-format"; fresh "$d"
sed -i -E 's/^## \[([0-9.]+)\]\(.*\) - /## [\1] - /' "$d/CHANGELOG.md"
expect_fail "a changelog with no readable headings fails" "$d" "none reads"

d="$tmp/no-markers"; fresh "$d"; sed -i '/generated features/d' "$d/README.md"
expect_fail "missing README markers fail" "$d" "README.md needs"
d="$tmp/swapped-markers"; fresh "$d"
awk '/BEGIN generated/{b=$0; next} /END generated/{print; print b; next} {print}' "$d/README.md" > "$d/r" && mv "$d/r" "$d/README.md"
expect_fail "markers out of order fail" "$d" "README.md needs"

# --- --release ---------------------------------------------------------------
d="$tmp/release"; fresh "$d"
run "$d" >/dev/null
sed -i 's|^## \[Unreleased\]|&\n\n## [0.3.0](https://example.com/compare/v0.2.0...v0.3.0) - 2026-11-03|' "$d/CHANGELOG.md"
out="$(run "$d" --release 0.3.0)" || fail "--release: $out"
[[ "$(sed -n 's/^since: //p' "$d/docs/features/gamma.md")" == "0.3.0" ]] \
  && [[ "$(sed -n 's/^since: //p' "$d/docs/features/alpha.md")" == "0.1.0" ]] \
  && [[ "$(sed -n 's/^since: //p' "$d/docs/features/beta.md")" == "0.2.0" ]] \
  && pass "--release stamps only the unreleased page" || fail "--release stamps"
[[ "$(jq -r '.features[] | select(.slug == "gamma") | .sinceDate' "$d/docs/feature-catalog.json")" == "2026-11-03" ]] \
  && pass "--release: sinceDate comes from the heading" || fail "--release sinceDate"
grep -qF '[Gamma feature](docs/features/gamma.md)' "$d/README.md" \
  && pass "--release drops (next release) from the README" || fail "--release README"
run "$d" --check >/dev/null && pass "--check passes after --release" || fail "--check after --release"

d="$tmp/release-missing"; fresh "$d"
expect_fail "--release without a heading fails" "$d" "no dated heading for 0.9.0" --release 0.9.0

echo
if [[ $fails -gt 0 ]]; then echo "$fails failed" >&2; exit 1; fi
echo "All passed"
