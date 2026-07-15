# JellyRock Companion: Jellyfin server plugin

The server-side companion for the [JellyRock](https://github.com/jellyrock/jellyrock) Roku client.
Named for the role, not a single feature. Server-assisted JellyRock capabilities live here.

## What it does today (issue #667)

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

The plain-HTTP path needs no plugin: there JellyRock opens Jellyfin's native session socket directly
(shipped in JellyRock #666). This plugin is only for HTTPS / remote servers.

**Wire contract:** the long-poll protocol is frozen and versioned in the JellyRock repo at
[`docs/architecture/remote-control-longpoll-contract.md`](https://github.com/jellyrock/jellyrock/blob/main/docs/architecture/remote-control-longpoll-contract.md).

## Requirements

- **JellyRock v2.23.0 or newer** on your Roku. The HTTPS remote-control support that consumes this
  plugin's long-poll channel shipped in JellyRock v2.23.0; earlier versions will not use the plugin.
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

None. The plugin has no settings: session matching is a fixed client identifier (`JellyRock`), and the
closed-app liveness window is derived from the client's own poll cadence (`2 × waitMs`), so there is
nothing to tune or misconfigure.

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
    RemoteControlController.cs        # the /JellyRock/RemoteControl long-poll + probe endpoints
    QueueingSessionController.cs      # ISessionController: queues commands + poll-liveness gating
    JellyRockSessionService.cs        # attaches the controller + forces the capability
    CommandEnvelope.cs                # the { MessageType, Data, MessageId } wire shape
Jellyfin.Plugin.JellyRock.Tests/      # xUnit tests (queue / liveness / enum-serialization / concurrency)
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
