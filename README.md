# JellyRock Companion: Jellyfin server plugin

The server-side companion for the [JellyRock](https://github.com/jellyrock/jellyrock) Roku client. Named
for the role, not a single feature — server-assisted JellyRock capabilities live here.

## Install

Install and auto-update from the Jellyfin Dashboard, no SSH or file copying:

1. **Dashboard → Plugins → Repositories → +**
2. Add a repository (any name) with this URL:

   ```text
   https://jellyrock.github.io/jellyfin-plugin-jellyrock/manifest.json
   ```

3. **Dashboard → Plugins → Catalog → General → JellyRock Companion → Install**, then restart Jellyfin
   when prompted.

Updates then surface in the Dashboard automatically. Each release's zip + `md5` are also attached to the
[GitHub releases](https://github.com/jellyrock/jellyfin-plugin-jellyrock/releases). For an unreleased
build, see [manual sideload](docs/dev/sideload.md).

## What it does

Three independent, server-assisted capabilities for JellyRock:

- **[Cold-launch cast](docs/features/cold-launch-cast.md)** — cast to a Roku even when JellyRock is
  *closed*. The plugin advertises the closed device as a "Play On" target and wakes it into the item over
  ECP. Works on http and https.
- **["Play On" remote control](docs/features/remote-control.md)** *(HTTPS)* — makes an *open* JellyRock a
  Play On / remote-control target on a secure server, where Roku can't open Jellyfin's `wss://` session
  socket. Bridges commands over a TLS long-poll.
- **[Fast playback cleanup](docs/features/playback-cleanup.md)** *(HTTPS)* — when you press Home
  mid-playback, reaps the lingering session in ~60s (instead of Jellyfin's ~5-10 min) and saves an
  accurate resume point.

## Do I need it?

- **Cold-launch cast** works on any server (http or https) that shares your Roku's LAN.
- **Remote control** and **playback cleanup** only do something on **HTTPS** servers — on plain http,
  JellyRock uses Jellyfin's native session socket directly, so neither needs a plugin.

## Settings

Two admin toggles (**Dashboard → Plugins → JellyRock Companion**), both **on by default**, both for
[cold-launch cast](docs/features/cold-launch-cast.md):

- **Show closed devices as cast targets** — whether a closed Roku appears in "Play On".
- **Include development builds** — whether sideloaded "JellyRock (dev)" builds are shown as closed
  targets (turn off for test/CI devices you never cast to).

The remote-control and playback-cleanup features have no settings.

## Requirements

- **Jellyfin server 10.11 or newer.** The plugin is a single `net9.0` assembly compiled against the
  10.11.0 API floor (`Jellyfin.Controller` / `Jellyfin.Model` and `targetAbi` are pinned there). Servers
  on 10.9/10.10 stay on the last `net8` / 10.9-ABI release (0.2.x) and are simply not offered newer
  versions — see [ADR 0023](https://github.com/jellyrock/jellyrock/blob/main/docs/adr/0023-cold-launch-cast-producer.md).
- **JellyRock on your Roku:** v2.23.0+ for cold-launch cast and remote control; v1.15.0+ for playback
  cleanup. Older clients fall back to Jellyfin's default behavior.
- **.NET 9 SDK** to build.

## Build

No local .NET SDK needed — build in the official SDK container:

```bash
docker run --rm -v "$PWD":/src -w /src --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet publish Jellyfin.Plugin.JellyRock/Jellyfin.Plugin.JellyRock.csproj -c Release -o /src/publish
# -> ./publish/Jellyfin.Plugin.JellyRock.dll
```

Only the plugin DLL is emitted; the `Jellyfin.*` assemblies and the ASP.NET Core shared framework are
supplied by the server at runtime (`ExcludeAssets=runtime` / `FrameworkReference`).

## Contributing

- **Releasing:** push a `release-x.y.z` branch. The **Release Preparation** workflow bumps
  `Directory.Build.props` + `build.yaml` in lockstep, rolls `CHANGELOG.md`'s `[Unreleased]` into a dated
  section, and opens a PR to `main`. Merging it tags `vx.y.z`, packages with
  [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), publishes the GitHub release
  (zip + `md5`), and refreshes the `manifest.json` so existing installs auto-update. Everyday work adds
  entries under `## [Unreleased]` in `CHANGELOG.md`.
- **Repo layout** and per-file notes: [docs/dev/layout.md](docs/dev/layout.md).
