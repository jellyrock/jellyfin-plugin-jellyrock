# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
On-disk versions are 4-part C# assembly versions (`x.y.z.0`); the headings and
git tags here use the 3-part `x.y.z` / `vx.y.z` form.

## [Unreleased]

### Added

- HTTPS "Cast to JellyRock" long-poll remote-control channel: forces
  `SupportsMediaControl` server-side for JellyRock sessions, attaches an
  `ISessionController` that queues fanned-out commands, and exposes an
  authenticated `GET /JellyRock/RemoteControl/poll` long-poll the Roku client
  consumes over TLS (no `wss://` required). Closed-app hygiene drops the cast
  target once polling stops. ([#667](https://github.com/jellyrock/jellyrock/issues/667))
- Server support for Jellyfin **10.9 – 10.11**: a single `net8.0` assembly
  compiled against the 10.9.0 API floor, with `targetAbi` pinned to `10.9.0.0`.
