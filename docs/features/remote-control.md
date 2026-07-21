# "Play On" remote control

Cast to and control an **open** JellyRock: play / pause / seek from the Jellyfin web or mobile app.
([JellyRock issue #667](https://github.com/jellyrock/jellyrock/issues/667))

This already works out of the box on **http** servers, where JellyRock opens Jellyfin's native session
socket directly. The plugin **extends it to https** servers, where Roku can't open a secure socket, so
you get the same Play On regardless of your server's scheme.

## Why a plugin is needed on HTTPS

Jellyfin pushes remote-control commands (`Play` / `Playstate` / `GeneralCommand`) to a session over a
**WebSocket**. Roku has no socket TLS (no `wss://`), so on a secure server JellyRock can't receive them
that way. On a **plain-http** server this feature needs no plugin: JellyRock opens Jellyfin's native
session socket directly (shipped in [JellyRock #666](https://github.com/jellyrock/jellyrock/issues/666)). The plugin only fills the gap on
https / remote servers.

## How it works

1. For each JellyRock session the plugin forces `SupportsMediaControl` server-side (JellyRock advertises
   it `false` on https, since it can't open the socket) and attaches an `ISessionController` that
   **queues** the commands the server fans out.
2. It exposes an authenticated **HTTP long-poll** channel (`GET /JellyRock/RemoteControl/poll`) that
   JellyRock consumes over TLS with `roUrlTransfer`, no `wss://` required.
3. **Closed-app hygiene:** the session is advertised as a cast target only while JellyRock is actively
   polling. When the app closes (or the poll loop dies), the liveness window lapses and Jellyfin's next
   cast-list query drops JellyRock automatically, no stale target left behind.

## Wire contract

The long-poll protocol is frozen and versioned in the JellyRock repo at
[`docs/architecture/remote-control-longpoll-contract.md`](https://github.com/jellyrock/jellyrock/blob/main/docs/architecture/remote-control-longpoll-contract.md).

## Requirements

An **HTTPS** Jellyfin server (10.11+) and JellyRock **v2.23.0 or newer** (the release that added the
HTTPS remote-control support that consumes this channel).
