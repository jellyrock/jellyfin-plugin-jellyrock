# Manual sideload (development only)

For iterating on an unreleased build, drop the DLL into the plugin folder by hand. For normal use,
install from the Dashboard instead (see the [README](../../README.md#install)).

Jellyfin discovers plugins by a **`meta.json`** in each plugin folder; the DLL + `meta.json` must be
owned by the Jellyfin runtime user (Jellyfin rewrites `meta.json` on load). Plugin dir =
`<jellyfin-data-dir>/plugins/` (`/config/data/plugins/` on the LinuxServer.io image). Confirm the exact
path from the running container rather than guessing:

```bash
docker exec <jellyfin-container> sh -c 'find /config -maxdepth 3 -type d -name plugins'
```

Then, from a machine with the built DLL (see [Build](README.md#build)) and SSH+Docker
access to the server:

```bash
HOST=<user>@<server>; CT=<jellyfin-container>
scp ./publish/Jellyfin.Plugin.JellyRock.dll "$HOST:/tmp/"
ssh "$HOST" bash -s <<'REMOTE'
  set -e
  # Match the folder name to the built version; check Directory.Build.props / build.yaml.
  DIR="/config/data/plugins/JellyRock Companion_0.2.0.0"
  docker exec "$CT" mkdir -p "$DIR"
  docker cp /tmp/Jellyfin.Plugin.JellyRock.dll "$CT:$DIR/Jellyfin.Plugin.JellyRock.dll"
  cat > /tmp/meta.json <<'META'
{ "category":"General", "changelog":"dev build",
  "description":"Server-side companion for the JellyRock Roku client.",
  "guid":"6c4f4b7e-50f7-43b8-a180-7de26f9033d6", "name":"JellyRock Companion",
  "overview":"Cold-launch cast, Play On remote control, and fast playback cleanup for JellyRock.",
  "owner":"jellyrock",
  "targetAbi":"10.11.0.0", "timestamp":"2026-07-20T00:00:00.0000000Z", "version":"0.2.0.0",
  "status":"Active", "autoUpdate":false, "imagePath":"", "assemblies":[] }
META
  docker cp /tmp/meta.json "$CT:$DIR/meta.json"
  docker exec "$CT" chown -R abc:abc "$DIR"   # abc = LinuxServer runtime user (PUID); adjust if bare-metal
  docker restart "$CT"
REMOTE
```

Confirm it loaded:

```bash
ssh "$HOST" 'docker logs '"$CT"' 2>&1 | grep -iE "Loaded plugin: JellyRock|cold-launch phantom service starting" | tail'
```

**Dashboard → Plugins** then lists **"JellyRock Companion"**, and **→ Settings** shows the cold-launch
cast toggles.
