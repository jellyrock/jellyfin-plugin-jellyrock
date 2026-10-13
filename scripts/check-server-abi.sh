#!/usr/bin/env bash
# Check that the built plugin binds against real Jellyfin server releases: every
# type and member it references exists in the server's binaries, and every server
# interface or abstract class it implements has nothing new it lacks. This is the
# check that lets one build on the targetAbi floor serve every newer server line
# (see "Server compatibility check" in docs/dev/README.md).
#
#   scripts/check-server-abi.sh                  every supported server line
#   scripts/check-server-abi.sh 12.2 10.11.11    just these releases
#   scripts/check-server-abi.sh --expect-fail 10.10.7
#                                                a release the plugin must NOT bind
#                                                to; proves the check can fail
#   --no-build                                   use the existing Release build
#   --list                                       print the supported releases and stop
#
# With no versions, it reads the release list on repo.jellyfin.org and checks the
# newest release of each server line from the floor in build.yaml's targetAbi up,
# so a new Jellyfin line is checked without anyone adding it.
#
# Runs the checker with the host's dotnet when there is one, else in the SDK
# container. Server releases are cached in $SERVER_ABI_CACHE (default
# ~/.cache/jellyfin-plugin-jellyrock/servers).
#
# Exit: 0 every release binds (or --list printed them); 1 a release does not
# (or, with --expect-fail, one does); 2 a release could not be checked.
set -euo pipefail

ROOT="$(git -C "${BASH_SOURCE%/*}" rev-parse --show-toplevel)"
BUILD_YAML="$ROOT/Jellyfin.Plugin.JellyRock/build.yaml"
PLUGIN_DLL="$ROOT/Jellyfin.Plugin.JellyRock/bin/Release/net9.0/Jellyfin.Plugin.JellyRock.dll"
CHECKER_DLL="$ROOT/tools/ServerAbiCheck/bin/Release/net9.0/ServerAbiCheck.dll"
REPO_URL="https://repo.jellyfin.org/files/server/linux/stable"
ARCHIVE_URL="https://repo.jellyfin.org/archive/server/linux/stable"
SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:9.0"
CACHE="${SERVER_ABI_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/jellyfin-plugin-jellyrock/servers}"

build=1
expect_fail=0
list_only=0
versions=()
for arg in "$@"; do
  case "$arg" in
    --no-build) build=0 ;;
    --list) list_only=1 ;;
    --expect-fail) expect_fail=1 ;;
    -h|--help) sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "::error::Unknown option: $arg" >&2; exit 2 ;;
    *) versions+=("$arg") ;;
  esac
done

mkdir -p "$CACHE"

run_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    dotnet "$@"
  else
    docker run --rm -v "$ROOT:$ROOT" -v "$CACHE:$CACHE" -w "$ROOT" \
      --user "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_NOLOGO=1 \
      "$SDK_IMAGE" dotnet "$@"
  fi
}

# "10.11.0.0" -> "10.11": the server line the plugin's targetAbi names.
floor_line() {
  grep -oP '^targetAbi:\s*"?\K[0-9]+\.[0-9]+' "$BUILD_YAML" | head -n1
}

# Is version line $1 at or above line $2?
line_at_least() {
  [[ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -n1)" == "$2" ]]
}

# The newest release of each server line from the floor up, one per line.
supported_versions() {
  local floor listing
  floor="$(floor_line)"
  if [[ -z "$floor" ]]; then
    echo "::error::Could not read targetAbi from $BUILD_YAML" >&2
    return 2
  fi
  listing="$(curl -fsSL --retry 3 "$REPO_URL/")" || {
    echo "::error::Could not read the release list at $REPO_URL/" >&2
    return 2
  }
  local all
  all="$(grep -oP 'href="v\K[0-9]+\.[0-9]+(\.[0-9]+)?(?=/")' <<<"$listing" | sort -V)"
  if [[ -z "$all" ]]; then
    # An empty list would check nothing and pass: the page format has changed.
    echo "::error::Found no releases in $REPO_URL/ (has its format changed?)" >&2
    return 2
  fi
  local version line found_floor=0 selected=()
  declare -A newest=()
  while read -r version; do
    line="$(cut -d. -f1-2 <<<"$version")"
    line_at_least "$line" "$floor" || continue
    [[ "$line" == "$floor" ]] && found_floor=1
    newest[$line]="$version"
  done <<<"$all"
  if (( ! found_floor )); then
    echo "::error::The floor line $floor (targetAbi) has no release in $REPO_URL/. Jellyfin has archived it: raise targetAbi or check that line by version." >&2
    return 2
  fi
  for line in "${!newest[@]}"; do selected+=("${newest[$line]}"); done
  printf '%s\n' "${selected[@]}" | sort -V
}

# Download and unpack one release's assemblies into the cache; print the directory.
server_dir() {
  local version="$1"
  local dir="$CACHE/$version"
  if [[ ! -f "$dir/.complete" ]]; then
    rm -rf "$dir"
    mkdir -p "$dir"
    local file="jellyfin_${version}-amd64.tar.gz"
    local tarball="$dir/$file"
    if ! curl -fsL --retry 3 -o "$tarball" "$REPO_URL/v$version/amd64/$file" \
      && ! curl -fsSL --retry 3 -o "$tarball" "$ARCHIVE_URL/v$version/amd64/$file"; then
      echo "::error::Could not download Jellyfin $version ($file) from $REPO_URL or $ARCHIVE_URL" >&2
      rm -rf "$dir"
      return 2
    fi
    tar -xzf "$tarball" -C "$dir" --wildcards 'jellyfin/*.dll'
    rm -f "$tarball"
    touch "$dir/.complete"
  fi
  echo "$dir/jellyfin"
}

if (( ${#versions[@]} == 0 )); then
  list="$(supported_versions)" || exit 2
  mapfile -t versions <<<"$list"
  if (( list_only )); then printf "%s\n" "${versions[@]}"; exit 0; fi
fi

if (( build )); then
  run_dotnet build "$ROOT/tools/ServerAbiCheck/ServerAbiCheck.csproj" -c Release --nologo -v quiet
  run_dotnet build "$ROOT/Jellyfin.Plugin.JellyRock/Jellyfin.Plugin.JellyRock.csproj" -c Release --nologo -v quiet
fi
for dll in "$PLUGIN_DLL" "$CHECKER_DLL"; do
  if [[ ! -f "$dll" ]]; then
    echo "::error::No build at $dll; run without --no-build" >&2
    exit 2
  fi
done

status=0
for version in "${versions[@]}"; do
  echo "== Jellyfin $version"
  dir="$(server_dir "$version")" || { status=2; continue; }
  set +e
  run_dotnet "$CHECKER_DLL" "$PLUGIN_DLL" "$dir"
  result=$?
  set -e
  if (( expect_fail )); then
    case "$result" in
      1) echo "✅ Does not bind to $version, as expected" ;;
      0) echo "::error::The plugin binds to $version, which it must not: the check can no longer see this break" >&2
         (( status == 2 )) || status=1 ;;
      *) status=2 ;;
    esac
  else
    case "$result" in
      0) echo "✅ Binds to $version" ;;
      1) echo "::error::The plugin does not bind to Jellyfin $version (problems above)" >&2
         (( status == 2 )) || status=1 ;;
      *) status=2 ;;
    esac
  fi
done
exit "$status"
