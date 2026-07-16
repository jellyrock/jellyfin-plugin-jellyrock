# JellyRock Companion: Jellyfin server plugin

The server-side companion for the [JellyRock](https://github.com/jellyrock/jellyrock) Roku client.
Named for the role, not a single feature. Server-assisted JellyRock capabilities live here.

## Features

The plugin bundles two independent server-assisted capabilities. **Neither has any settings.**

### 1. "Play On" cast + remote control — HTTPS servers ([JellyRock issue #667](https://github.com/jellyrock/jellyrock/issues/667))

Makes the JellyRock Roku app a remote-control **"Play On"** target on an **HTTPS** server.

Jellyfin pushes remote-control commands (`Play` / `Playstate` / `GeneralCommand`) to a session over a
**WebSocket**. Roku has no socket TLS (no `wss://`), so on a secure server JellyRock can't receive
them that way. This plugin bridges the gap:

1. For each JellyRock session it forces `SupportsMediaControl` server-side (JellyRock advertises it
   `false` on https, since it can't open the socket) and attaches an `ISessionController` that
   **queues** the commands the server fans out.
2. It exposes an authenticated **HTTP long-poll** channel (`GET /JellyRock/RemoteControl/poll`) that
   JellyRock consumes over TLS with `roUrlTransfer`, no `wss://` required.
3. **Closed-app hygiene:** the session is advertised as a cast target only while JellyRock is actively
   polling. When the app closes (or the poll loop dies), the liveness window lapses and Jellyfin's
   next cast-list query drops JellyRock automatically, no stale target left behind.

On a **plain-HTTP** server this feature needs no plugin — there JellyRock opens Jellyfin's native
session socket directly (shipped in [JellyRock #666](https://github.com/jellyrock/jellyrock/issues/666)) — so it only does something on HTTPS / remote
servers.

**Wire contract:** the long-poll protocol is frozen and versioned in the JellyRock repo at
[`docs/architecture/remote-control-longpoll-contract.md`](https://github.com/jellyrock/jellyrock/blob/main/docs/architecture/remote-control-longpoll-contract.md).

### 2. Fast closed-app playback cleanup — HTTPS servers ([JellyRock issue #43](https://github.com/jellyrock/jellyrock/issues/43))

Press **Home** on the Roku mid-playback and the app is torn down instantly, with no chance to tell the
server it stopped. On an **HTTP** server this already resolves itself: JellyRock holds Jellyfin's
native session socket, so when the app closes the socket drops and the server removes the session
within seconds (this is what fixed issue #43 for HTTP, via JellyRock's `ws://` support). On an
**HTTPS** server Roku can't open that socket (no `wss://`), so nothing signals the close and Jellyfin
keeps the transcode running with a phantom "now playing" until its own idle check reaps it ~5-10
minutes later. This plugin shortens that to **~60 seconds**.

A lightweight background sweep watches JellyRock sessions; when one is actively playing but its
playback check-ins (the client's ~10s `Sessions/Playing/Progress` reports) have gone silent past a
60-second threshold, it stops the session the same way Jellyfin's own idle reaper does, just on a
faster clock.

It records the resume point at the **last confirmed check-in** position rather than Jellyfin's
forward-extrapolated one, so resuming later lands where you actually stopped instead of skipping
ahead. Paused sessions are left untouched (Jellyfin's own paused/idle handling covers those).

## Do I need it?

**Only on HTTPS servers.** Both features address problems that exist specifically because Roku can't
open a secure socket (`wss://`):

- **Casting / remote control ("Play On" to Roku):** the plugin bridges cast commands over TLS. On a
  plain-HTTP server this needs no plugin (JellyRock uses Jellyfin's native session socket directly).
- **Fast closed-app playback cleanup:** on HTTPS a closed app leaves its session lingering ~5-10
  minutes; the plugin reaps it in ~60 seconds. On HTTP that same native session socket already removes
  the session within seconds when the app closes, so the plugin has nothing to do there.

So: install it on an **HTTPS** server. On plain HTTP you don't need it for either feature.

## Requirements

- **JellyRock on your Roku.** The fast closed-app playback cleanup (feature 2) requires **JellyRock
  v1.15.0 or newer** — the release that moved playback progress reporting to a ~10s cadence, which the
  cleanup relies on to detect a closed app safely; on older clients the plugin does nothing and
  Jellyfin's default idle handling applies (unchanged). The "Play On" cast + remote control (feature 1)
  requires **JellyRock v2.23.0 or newer** — the release that added the HTTPS remote-control support
  that consumes this plugin's long-poll channel.
- Jellyfin server **10.9-10.11**. The plugin is a single `net8.0` assembly compiled against the
  **10.9.0** API floor (`Jellyfin.Controller` / `Jellyfin.Model` and `targetAbi` are pinned there),
  which the compiler uses to guarantee only API present in every supported server is used. A net8
  assembly loads on 10.9/10.10 (net8) and 10.11 (net9), and the session API it relies on is identical
  across that range. **12.0** (net10, currently RC) keeps the same session API and should load a net8
  build, but is not yet verified on-device, so treat it as unsupported until confirmed.
- .NET 8 SDK to build.

## Build

No local .NET SDK needed. Build in the official SDK container:

```bash
docker run --rm -v "$PWD":/src -w /src --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet publish Jellyfin.Plugin.JellyRock/Jellyfin.Plugin.JellyRock.csproj -c Release -o /src/publish
# -> ./publish/Jellyfin.Plugin.JellyRock.dll
```

The publish folder contains only the plugin DLL. The `Jellyfin.*` assemblies and the ASP.NET Core
shared framework are supplied by the server at runtime (`ExcludeAssets=runtime` / `FrameworkReference`).

## Install

Install and auto-update straight from the Jellyfin Dashboard, no SSH or file copying.

1. **Dashboard → Plugins → Repositories → +**
2. Add the JellyRock plugin repository (any name), with this URL:

   ```text
   https://jellyrock.github.io/jellyfin-plugin-jellyrock/manifest.json
   ```
3. **Dashboard → Plugins → Catalog → General → JellyRock Companion → Install**, then restart Jellyfin
   when prompted.

Future versions surface as updates in the Dashboard automatically. Each release's zip + `md5` are also
attached to the corresponding [GitHub release](https://github.com/jellyrock/jellyfin-plugin-jellyrock/releases)
if you prefer to grab them directly.

<details>
<summary>Manual sideload (development only)</summary>

For iterating on an unreleased build, drop the DLL into the plugin folder by hand. Jellyfin discovers
plugins by a **`meta.json`** in each plugin folder; the DLL + `meta.json` must be owned by the Jellyfin
runtime user (Jellyfin rewrites `meta.json` on load). Plugin dir = `<jellyfin-data-dir>/plugins/`
(`/config/data/plugins/` on the LinuxServer.io image). Confirm the exact path from the running container
rather than guessing:

```bash
docker exec <jellyfin-container> sh -c 'find /config -maxdepth 3 -type d -name plugins'
```

Then, from a machine with the built DLL and SSH+Docker access to the server:

```bash
HOST=<user>@<server>; CT=<jellyfin-container>
scp ./publish/Jellyfin.Plugin.JellyRock.dll "$HOST:/tmp/"
ssh "$HOST" bash -s <<'REMOTE'
  set -e
  DIR=/config/data/plugins/JellyRock_0.1.0.0
  docker exec "$CT" mkdir -p "$DIR"
  docker cp /tmp/Jellyfin.Plugin.JellyRock.dll "$CT:$DIR/Jellyfin.Plugin.JellyRock.dll"
  cat > /tmp/meta.json <<'META'
{ "category":"General", "changelog":"0.1.0: HTTPS cast long-poll channel (#667)",
  "description":"Server-side companion for the JellyRock Roku client.",
  "guid":"6c4f4b7e-50f7-43b8-a180-7de26f9033d6", "name":"JellyRock Companion",
  "overview":"Makes JellyRock a Play-On target on HTTPS.", "owner":"jellyrock",
  "targetAbi":"10.9.0.0", "timestamp":"2026-07-13T00:00:00.0000000Z", "version":"0.1.0.0",
  "status":"Active", "autoUpdate":false, "imagePath":"", "assemblies":[] }
META
  docker cp /tmp/meta.json "$CT:$DIR/meta.json"
  docker exec "$CT" chown -R abc:abc "$DIR"   # abc = LinuxServer runtime user (PUID); adjust if bare-metal
  docker restart "$CT"
REMOTE
```

Confirm it loaded:

```bash
ssh "$HOST" 'docker logs '"$CT"' 2>&1 | grep -iE "Loaded plugin: JellyRock|JellyRock. session service" | tail'
```

**Dashboard → Plugins** lists **"JellyRock Companion"**.
</details>

## Configuration

None. The plugin has no settings: session matching is a fixed client identifier (`JellyRock`); the
cast-target liveness window is derived from the client's own poll cadence (`2 × waitMs`); and the
playback-cleanup threshold is a fixed 60 seconds (matching Jellyfin's own hardcoded idle reap, just
faster). Nothing to tune or misconfigure.

## Layout

```
CHANGELOG.md                          # Keep a Changelog; release notes come from here
Directory.Build.props                 # assembly <Version> (kept in lockstep with build.yaml)
jellyfin.ruleset / .editorconfig      # analyzer + style config (StyleCop, etc.)
Jellyfin.Plugin.JellyRock.sln
scripts/                              # release helpers (set-version, changelog-extract, parity)
.github/workflows/                    # ci.yml, release-prepare.yml, release.yml
Jellyfin.Plugin.JellyRock/
  build.yaml                          # plugin manifest (guid, targetAbi, artifact, version); jprm reads this
  Jellyfin.Plugin.JellyRock.csproj
  Plugin.cs                           # BasePlugin<PluginConfiguration> (no config page)
  PluginServiceRegistrator.cs         # registers the session service into DI
  Configuration/PluginConfiguration.cs  # empty (BasePlugin requires the type)
  RemoteControl/
    RemoteControlController.cs        # the /JellyRock/RemoteControl long-poll + probe endpoints (feature 1)
    QueueingSessionController.cs      # ISessionController: queues commands + poll-liveness gating (feature 1)
    JellyRockSessionService.cs        # attaches the controller + forces the capability (feature 1)
    CommandEnvelope.cs                # the { MessageType, Data, MessageId } wire shape (feature 1)
    PlaybackReaperService.cs          # sweeps + reaps idle JellyRock playback sessions (feature 2, jellyrock#43)
    ReapDecision.cs                   # pure reap-eligibility rule + resume-position correction (feature 2)
Jellyfin.Plugin.JellyRock.Tests/      # xUnit tests (queue / liveness / serialization / concurrency / reaper)
```

## Releasing

Cutting a release is two steps (same flow as the JellyRock app, see
[issue #3](https://github.com/jellyrock/jellyfin-plugin-jellyrock/issues/3)):

1. **Push a `release-x.y.z` branch.** The **Release Preparation** workflow bumps
   `Directory.Build.props` + `build.yaml` in lockstep, rolls `CHANGELOG.md`'s `[Unreleased]` into a
   dated section on that branch, and opens a PR to `main`.

   ```bash
   git switch main && git pull
   git switch -c release-0.2.0 && git push -u origin release-0.2.0
   ```
2. Review and **merge** that PR. `release.yml` then tags `vx.y.z`, packages the plugin with
   [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), publishes a GitHub release
   (zip + `md5`, notes from `CHANGELOG.md`), and refreshes the `manifest.json` served from GitHub Pages
   so existing installs auto-update.

Everyday work adds entries under `## [Unreleased]` in `CHANGELOG.md`; the release rolls them up.
