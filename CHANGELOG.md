<!-- markdownlint-disable -->
# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
On-disk versions are 4-part C# assembly versions (`x.y.z.0`); the headings and
git tags here use the 3-part `x.y.z` / `vx.y.z` form.

## [Unreleased]

### Changed

- Cold-launch cast producer: phantom session, pairing, validation ([#9](https://github.com/jellyrock/jellyfin-plugin-jellyrock/pull/9))
## [0.2.0](https://github.com/jellyrock/jellyfin-plugin-jellyrock/compare/v0.1.2...v0.2.0) - 2026-07-16

- reap idle JellyRock playback sessions on HTTPS servers ([#7](https://github.com/jellyrock/jellyfin-plugin-jellyrock/pull/7))

## [0.1.2](https://github.com/jellyrock/jellyfin-plugin-jellyrock/compare/v0.1.1...v0.1.2) - 2026-07-15

### Changed

- Plugin catalog listing: clearer user-facing description, set the maintainer to
  Charles Ewert, and added the JellyRock icon. Documented that the plugin requires
  JellyRock v2.23.0 or newer (the release that added HTTPS remote-control support).

## [0.1.1](https://github.com/jellyrock/jellyfin-plugin-jellyrock/compare/v0.1.0...v0.1.1) - 2026-07-15

### Added

- Release automation: push a `release-x.y.z` branch to bump the version, package
  the plugin with [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager),
  publish a GitHub release, and refresh the plugin-repo `manifest.json` so the
  Jellyfin Dashboard can install and auto-update the plugin.
  ([#3](https://github.com/jellyrock/jellyfin-plugin-jellyrock/issues/3))

### Changed

- Install: the primary path is now the Jellyfin Dashboard plugin repository; the
  manual `scp`/`docker cp` sideload is kept as a developer note.

## [0.1.0](https://github.com/jellyrock/jellyfin-plugin-jellyrock/releases/tag/v0.1.0) - 2026-07-14

### Added

- HTTPS "Cast to JellyRock" long-poll remote-control channel: forces
  `SupportsMediaControl` server-side for JellyRock sessions, attaches an
  `ISessionController` that queues fanned-out commands, and exposes an
  authenticated `GET /JellyRock/RemoteControl/poll` long-poll the Roku client
  consumes over TLS (no `wss://` required). Closed-app hygiene drops the cast
  target once polling stops. ([#667](https://github.com/jellyrock/jellyrock/issues/667))
- Server support for Jellyfin **10.9-10.11**: a single `net8.0` assembly
  compiled against the 10.9.0 API floor, with `targetAbi` pinned to `10.9.0.0`.
