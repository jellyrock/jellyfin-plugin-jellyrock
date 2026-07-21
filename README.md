# JellyRock Companion: Jellyfin server plugin

The server-side companion for the [JellyRock](https://github.com/jellyrock/jellyrock) Roku client.

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
[GitHub releases](https://github.com/jellyrock/jellyfin-plugin-jellyrock/releases).

## What it does

Three independent, server-assisted capabilities for JellyRock:

- **[Cold-launch cast](docs/features/cold-launch-cast.md)**: cast to a Roku even when JellyRock is
  *closed*. The plugin advertises the closed device as a "Play On" target and wakes it into the item over
  ECP. Works on http and https.
- **["Play On" remote control](docs/features/remote-control.md)**: cast to and control an *open*
  JellyRock. This already works out of the box on **http** servers (JellyRock uses Jellyfin's native
  session socket); the plugin **extends it to https** servers, where Roku can't open a secure `wss://`
  socket, by bridging commands over a TLS long-poll.
- **[Fast playback cleanup](docs/features/playback-cleanup.md)**: reap a closed app's playback session
  fast. Already near-instant on **http** with no plugin; the plugin **extends the same cleanup to https**,
  reaping the lingering session in ~60s (instead of Jellyfin's ~5-10 min) with an accurate resume point.

## Do I need it?

- **Cold-launch cast** needs the plugin on any server (http or https) that shares your Roku's LAN.
- **Remote control** and **playback cleanup** already work on **http** with no plugin (JellyRock uses
  Jellyfin's native session socket). Install the plugin for these two only if your server is **https**,
  where Roku can't open that socket and the plugin fills the gap.

## Settings

Two admin toggles (**Dashboard → Plugins → JellyRock Companion**), both **on by default**, both for
[cold-launch cast](docs/features/cold-launch-cast.md):

- **Show closed devices as cast targets**: whether a closed Roku appears in "Play On".
- **Include development builds**: whether sideloaded "JellyRock (dev)" builds are shown as closed
  targets (turn off for test/CI devices you never cast to).

The remote-control and playback-cleanup features have no settings.

## Requirements

- **Jellyfin server 10.11 or newer.** The plugin is a single `net9.0` assembly compiled against the
  10.11.0 API floor (`Jellyfin.Controller` / `Jellyfin.Model` and `targetAbi` are pinned there). Servers
  on 10.9/10.10 stay on the last `net8` / 10.9-ABI release (0.2.x) and are simply not offered newer
  versions. See [ADR 0023](https://github.com/jellyrock/jellyrock/blob/main/docs/adr/0023-cold-launch-cast-producer.md).
- **JellyRock on your Roku:** v2.23.0+ for cold-launch cast and remote control; v1.15.0+ for playback
  cleanup. Older clients fall back to Jellyfin's default behavior.

## Development

Building the plugin, the manual sideload, repo layout, and the maintainer release runbook live in
[docs/dev](docs/dev/).
