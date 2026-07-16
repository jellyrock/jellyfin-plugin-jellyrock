<!-- markdownlint-disable -->
# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
On-disk versions are 4-part C# assembly versions (`x.y.z.0`); the headings and
git tags here use the 3-part `x.y.z` / `vx.y.z` form.

## [Unreleased]

### Added

- Fast closed-app playback cleanup for HTTPS servers: a background sweep stops a
  JellyRock playback session ~60 seconds after the Roku app is closed mid-video,
  instead of waiting ~5-10 minutes for Jellyfin's own idle check, freeing the transcode
  and clearing the phantom "now playing". It records the resume point at the last
  confirmed position so resuming lands where you stopped rather than skipping ahead. On
  HTTP the issue is already handled by JellyRock's native session socket (the socket
  drops on app close and the server removes the session within seconds), so this targets
  the HTTPS case where no such socket exists. Requires JellyRock v1.15.0 or newer (the
  release that moved to a ~10s playback-report cadence); paused sessions are left to
  Jellyfin's own handling.
  ([#43](https://github.com/jellyrock/jellyrock/issues/43))

## [0.1.2] - 2026-07-15

### Changed

- Plugin catalog listing: clearer user-facing description, set the maintainer to
  Charles Ewert, and added the JellyRock icon. Documented that the plugin requires
  JellyRock v2.23.0 or newer (the release that added HTTPS remote-control support).

## [0.1.1] - 2026-07-15

### Added

- Release automation: push a `release-x.y.z` branch to bump the version, package
  the plugin with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager),
  publish a GitHub release, and refresh the plugin-repo `manifest.json` so the
  Jellyfin Dashboard can install and auto-update the plugin.
  ([#3](https://github.com/jellyrock/jellyfin-plugin-jellyrock/issues/3))

### Changed

- Install: the primary path is now the Jellyfin Dashboard plugin repository; the
  manual `scp`/`docker cp` sideload is kept as a developer note.

## [0.1.0] - 2026-07-14

### Added

- HTTPS "Cast to JellyRock" long-poll remote-control channel: forces
  `SupportsMediaControl` server-side for JellyRock sessions, attaches an
  `ISessionController` that queues fanned-out commands, and exposes an
  authenticated `GET /JellyRock/RemoteControl/poll` long-poll the Roku client
  consumes over TLS (no `wss://` required). Closed-app hygiene drops the cast
  target once polling stops. ([#667](https://github.com/jellyrock/jellyrock/issues/667))
- Server support for Jellyfin **10.9-10.11**: a single `net8.0` assembly
  compiled against the 10.9.0 API floor, with `targetAbi` pinned to `10.9.0.0`.
