#!/usr/bin/env bash
# Generate the feature catalog and the text that repeats it, from the frontmatter of
# docs/features/*.md (docs/dev/feature-pages.md has the field rules).
#
#   Usage: scripts/feature-catalog.sh [--check | --release <x.y.z>] [--root <dir>]
#
#   (no flag)         write the outputs that changed and say which
#   --check           write nothing; fail naming each stale output
#   --release x.y.z   first change `since: unreleased` to `since: x.y.z` in every page
#                     (CHANGELOG.md must already have the dated heading), then write
#   --root <dir>      the tree to read and write (default: this repo); the test uses it
#
# Reads:
#   docs/features/*.md                       frontmatter between the first two --- lines
#   CHANGELOG.md                             release dates, from `## [x.y.z](link) - DATE`
#   Jellyfin.Plugin.JellyRock/build.yaml     targetAbi (the server floor)
# Writes:
#   docs/feature-catalog.json                the catalog the jellyrock.app site reads
#   README.md                                the block between the generated-features markers
#   Jellyfin.Plugin.JellyRock/build.yaml     the description (one line)
#   docs/features/*.md                       only with --release: the since stamp
#
# Exit: 0 done (or --check clean); 1 a page is invalid or an output is stale; 2 bad usage.
set -euo pipefail
export LC_ALL=C.UTF-8   # the pitch length counts characters; sort order is by code point

# One constant, so a fork or a clone with another remote writes the same catalog.
REPO_URL="https://github.com/jellyrock/jellyfin-plugin-jellyrock"
PITCH_MAX=120
BEGIN_MARK='<!-- BEGIN generated features (scripts/feature-catalog.sh) -->'
END_MARK='<!-- END generated features -->'

check=0
release=""
root="$(cd "${BASH_SOURCE%/*}/.." && pwd)"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --check) check=1; shift ;;
    --release) release="${2:?--release needs x.y.z}"; shift 2 ;;
    --root) root="${2:?--root needs a directory}"; shift 2 ;;
    -h|--help) sed -n '2,22p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "::error::Unknown option: $1" >&2; exit 2 ;;
  esac
done
if [[ $check == 1 && -n "$release" ]]; then
  echo "::error::--check and --release cannot be used together" >&2
  exit 2
fi
if [[ -n "$release" && ! "$release" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::--release needs a 3-part version x.y.z (got '$release')" >&2
  exit 2
fi

pages_dir="$root/docs/features"
changelog="$root/CHANGELOG.md"
yaml="$root/Jellyfin.Plugin.JellyRock/build.yaml"
readme="$root/README.md"
catalog="$root/docs/feature-catalog.json"
semver='^[0-9]+\.[0-9]+\.[0-9]+$'
fields=(title pitch since jellyrock http https)

# --- CHANGELOG release dates -------------------------------------------------
declare -A dates=()
heading_re='^## \[([0-9]+\.[0-9]+\.[0-9]+)\]\([^)]*\) - ([0-9]{4}-[0-9]{2}-[0-9]{2})$'
if [[ -f "$changelog" ]]; then
  while IFS= read -r line; do
    [[ "$line" =~ $heading_re ]] && dates["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
  done < "$changelog"
  if [[ ${#dates[@]} -eq 0 ]] && grep -qE '^## \[[0-9]' "$changelog"; then
    echo "::error::CHANGELOG.md has release headings but none reads '## [x.y.z](link) - YYYY-MM-DD'; every sinceDate would be missing." >&2
    exit 1
  fi
fi

# --- --release: stamp the unreleased pages -----------------------------------
mapfile -t pages < <(find "$pages_dir" -maxdepth 1 -name '*.md' | sort)
if [[ ${#pages[@]} -eq 0 ]]; then
  echo "::error::No pages in docs/features/" >&2
  exit 1
fi

if [[ -n "$release" ]]; then
  if [[ -z "${dates[$release]:-}" ]]; then
    echo "::error::CHANGELOG.md has no dated heading for $release; roll the changelog first." >&2
    exit 1
  fi
  for page in "${pages[@]}"; do
    tmp="$(mktemp)"
    awk -v ver="$release" '
      /^---$/ { fence++ }
      fence == 1 && $0 == "since: unreleased" { print "since: " ver; next }
      { print }
    ' "$page" > "$tmp"
    if cmp -s "$tmp" "$page"; then rm -f "$tmp"; else cat "$tmp" > "$page"; rm -f "$tmp"; fi
  done
fi

# --- read and validate the pages ---------------------------------------------
errors=()
objs=()
for page in "${pages[@]}"; do
  rel="docs/features/${page##*/}"
  slug="${page##*/}"; slug="${slug%.md}"
  declare -A fm=()
  bad=0
  if [[ "$(head -n1 "$page")" != "---" ]]; then
    errors+=("$rel: the page does not start with a --- frontmatter block"); continue
  fi
  closed=0
  while IFS= read -r line; do
    if [[ "$line" == "---" ]]; then closed=1; break; fi
    if [[ "$line" =~ ^([a-z]+):\ (.*)$ ]]; then
      key="${BASH_REMATCH[1]}"
      if [[ -n "${fm[$key]+x}" ]]; then errors+=("$rel: duplicate field '$key'"); bad=1; continue; fi
      fm[$key]="${BASH_REMATCH[2]}"
    else
      errors+=("$rel: frontmatter line is not 'key: value': $line"); bad=1
    fi
  done < <(tail -n +2 "$page")
  if [[ $closed == 0 ]]; then errors+=("$rel: the frontmatter block has no closing ---"); unset fm; continue; fi

  for key in "${!fm[@]}"; do
    case " ${fields[*]} " in *" $key "*) ;; *) errors+=("$rel: unknown field '$key'"); bad=1 ;; esac
  done
  for key in "${fields[@]}"; do
    [[ -n "${fm[$key]+x}" ]] || { errors+=("$rel: missing field '$key'"); bad=1; }
  done

  if [[ -n "${fm[title]+x}" && -z "${fm[title]}" ]]; then errors+=("$rel: empty title"); bad=1; fi
  if [[ -n "${fm[pitch]+x}" ]]; then
    if [[ -z "${fm[pitch]}" ]]; then errors+=("$rel: empty pitch"); bad=1
    elif (( ${#fm[pitch]} > PITCH_MAX )); then
      errors+=("$rel: pitch is ${#fm[pitch]} characters; the limit is $PITCH_MAX"); bad=1
    fi
  fi
  for key in http https; do
    if [[ -n "${fm[$key]+x}" && "${fm[$key]}" != "needed" && "${fm[$key]}" != "not-needed" ]]; then
      errors+=("$rel: $key must be 'needed' or 'not-needed' (got '${fm[$key]}')"); bad=1
    fi
  done
  if [[ -n "${fm[jellyrock]+x}" && ! "${fm[jellyrock]}" =~ $semver ]]; then
    errors+=("$rel: jellyrock must be x.y.z (got '${fm[jellyrock]}')"); bad=1
  fi
  since_date=""
  if [[ -n "${fm[since]+x}" ]]; then
    if [[ "${fm[since]}" == "unreleased" ]]; then
      :
    elif [[ "${fm[since]}" =~ $semver ]]; then
      since_date="${dates[${fm[since]}]:-}"
      if [[ -z "$since_date" ]]; then
        errors+=("$rel: since ${fm[since]} has no dated heading in CHANGELOG.md"); bad=1
      fi
    else
      errors+=("$rel: since must be 'unreleased' or x.y.z (got '${fm[since]}')"); bad=1
    fi
  fi

  if [[ $bad == 0 ]]; then
    objs+=("$(jq -nc \
      --arg slug "$slug" --arg title "${fm[title]}" --arg pitch "${fm[pitch]}" \
      --arg since "${fm[since]}" --arg sinceDate "$since_date" --arg jellyrock "${fm[jellyrock]}" \
      --arg http "${fm[http]}" --arg https "${fm[https]}" --arg url "$REPO_URL/blob/main/docs/features/$slug.md" \
      '{slug: $slug, title: $title, pitch: $pitch, since: $since,
        sinceDate: (if $sinceDate == "" then null else $sinceDate end),
        jellyrock: $jellyrock, http: $http, https: $https, url: $url}')")
  fi
  unset fm
done

# --- targetAbi -> plugin.server ----------------------------------------------
server=""
if [[ -f "$yaml" ]]; then
  server="$(grep -oP '^targetAbi:\s*"?\K[0-9]+\.[0-9]+\.[0-9]+' "$yaml" | head -n1 || true)"
fi
[[ -n "$server" ]] || errors+=("Jellyfin.Plugin.JellyRock/build.yaml: could not read targetAbi")

if [[ ${#errors[@]} -gt 0 ]]; then
  for e in "${errors[@]}"; do echo "::error::$e" >&2; done
  exit 1
fi

# --- build the outputs in a temp dir -----------------------------------------
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

features="$(printf '%s\n' "${objs[@]}" | jq -s 'sort_by(.title)')"

jq -n --argjson features "$features" --arg server "$server" '{
  _comment: "Generated from the frontmatter of docs/features/*.md by scripts/feature-catalog.sh. Edit the pages, not this file.",
  schema: 1,
  plugin: {server: $server},
  features: $features
}' > "$out/catalog"

# README block: a table, one row per feature in catalog order.
{
  echo "$BEGIN_MARK"
  echo
  echo "| Feature | What you get | HTTP server | HTTPS server | JellyRock |"
  echo "| --- | --- | --- | --- | --- |"
  jq -r '
    def cell: if . == "needed" then "Plugin needed" else "Works without it" end;
    def esc: gsub("\\|"; "\\|");
    .features[] |
    "| [\((.title + (if .since == "unreleased" then " (next release)" else "" end)) | esc)](docs/features/\(.slug).md)"
    + " | \(.pitch | esc) | \(.http | cell) | \(.https | cell) | \(.jellyrock) or newer |"
  ' "$out/catalog"
  echo
  echo "$END_MARK"
} > "$out/block"

begin_line="$(grep -nxF -- "$BEGIN_MARK" "$readme" | head -n1 | cut -d: -f1 || true)"
end_line="$(grep -nxF -- "$END_MARK" "$readme" | head -n1 | cut -d: -f1 || true)"
if [[ -z "$begin_line" || -z "$end_line" || "$begin_line" -ge "$end_line" ]]; then
  echo "::error::README.md needs '$BEGIN_MARK' above '$END_MARK'" >&2
  exit 1
fi
{
  head -n "$((begin_line - 1))" "$readme"
  cat "$out/block"
  tail -n +"$((end_line + 1))" "$readme"
} > "$out/readme"

# build.yaml description: one line, a JSON string (valid YAML, safe for any character).
desc="$(jq -r '
  def where: if .http == "needed" and .https == "needed" then "Needed on HTTP and HTTPS servers"
    elif .https == "needed" then "Needed on HTTPS servers"
    elif .http == "needed" then "Needed on HTTP servers"
    else "Not needed on any server" end;
  "The server-side companion for the JellyRock Roku app."
  + ([.features[] | " \(.title): \(.pitch) \(where), with JellyRock \(.jellyrock) or newer."] | join(""))
' "$out/catalog")"
if [[ "$(grep -c '^description:' "$yaml")" != 1 ]]; then
  echo "::error::build.yaml needs exactly one 'description:' key" >&2
  exit 1
fi
# ENVIRON, not awk -v: -v would read the backslashes of a JSON escape as its own.
DESC_LINE="description: $(jq -nr --arg d "$desc" '$d|tojson')" awk '
  /^description:/ { print ENVIRON["DESC_LINE"]; skip = 1; next }
  skip && /^[[:space:]]/ { next }
  { skip = 0; print }
' "$yaml" > "$out/yaml"

# --- write, or compare -------------------------------------------------------
stale=()
sync() {  # <generated file> <target> <name>
  if [[ -f "$2" ]] && cmp -s "$1" "$2"; then return; fi
  stale+=("$3")
  if [[ $check == 0 ]]; then cat "$1" > "$2"; fi
}
sync "$out/catalog" "$catalog" "docs/feature-catalog.json"
sync "$out/readme" "$readme" "README.md"
sync "$out/yaml" "$yaml" "Jellyfin.Plugin.JellyRock/build.yaml"

if [[ $check == 1 ]]; then
  if [[ ${#stale[@]} -gt 0 ]]; then
    for s in "${stale[@]}"; do echo "::error::$s is out of date with docs/features/" >&2; done
    echo "Run scripts/feature-catalog.sh" >&2
    exit 1
  fi
  echo "✅ Feature catalog, README block and build.yaml description are current"
elif [[ ${#stale[@]} -gt 0 ]]; then
  for s in "${stale[@]}"; do echo "wrote $s"; done
else
  echo "✅ Nothing to change"
fi
