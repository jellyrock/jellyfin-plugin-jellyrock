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

The plugin adds server-side help for JellyRock. Each feature has a page with the details; the table says which servers need the plugin.

<!-- BEGIN generated features (scripts/feature-catalog.sh) -->

| Feature | What you get | HTTP server | HTTPS server | JellyRock |
| --- | --- | --- | --- | --- |
| [Cast from another Jellyfin app](docs/features/remote-control.md) | Cast from a Jellyfin app on your phone or browser to an open JellyRock, then pause, seek and skip from there. | Works without it | Plugin needed | 2.23.0 or newer |
| [Cast while JellyRock is closed](docs/features/cold-launch-cast.md) | Cast from a Jellyfin app to your Roku while JellyRock is closed, when your server is on the same network as the Roku. | Plugin needed | Plugin needed | 2.24.0 or newer |
| [Fast cleanup after you press Home](docs/features/playback-cleanup.md) | Press Home on your Roku mid-playback and your server ends the session within a minute instead of 5 to 10 minutes. | Works without it | Plugin needed | 2.23.0 or newer |
| [SyncPlay (watch together)](docs/features/syncplay.md) | Watch together in a Jellyfin SyncPlay group, and the group stops waiting on your Roku once you close JellyRock. | Works without it | Plugin needed | 3.3.0 or newer |

<!-- END generated features -->

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

## Development

Building the plugin, the commit and PR-title style, the manual sideload, repo layout, and the maintainer release runbook live in
[docs/dev](docs/dev/).
