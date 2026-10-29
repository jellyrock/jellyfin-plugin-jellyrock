# ADR 0001: When a long-poll JellyRock app counts as closed

- **Status:** Accepted
- **Date:** 2026-10-29
- **Deciders:** maintainer

## Context

On an HTTPS server JellyRock reaches Jellyfin through this plugin's long poll, because Roku has no
`wss://`. A Roku app can't run code when the user presses **Home**, and no socket drops on HTTPS, so
the server never learns the app closed. A closed app stayed in its SyncPlay group, which then waited
on it at every seek or item change (until removed on 10.11 and 12.0, up to 30 seconds from 12.1). It
also kept a paused "now playing" until the server's idle check, 5 to 10 minutes later.

## Decision

A JellyRock app counts as closed when it has polled the plugin at least once and no controller of its
session other than the cold-launch phantom is active (`PhantomSessionService.IsAppOpen`). The poll
window is two poll waits. When the app closes a poll (the request's abort token) and no other poll is
open, the window shrinks to a 10-second grace (`QueueingSessionController.MarkPollEnded`).

The SyncPlay leave and the playback reap (playing or paused, `ReapDecision.IsClosedAppReapable`) ask
`ReapDecision.IsLongPollAppClosed`, and the cast list reads the same poll window through
`QueueingSessionController.IsSessionActive`, so all three answer "is it gone?" the same way.

## Consequences

- A closed app leaves its group, and its playback is reaped, within seconds. Measured on 2026-10-29
  on a Streaming Stick 4K with JellyRock 3.3.0 and Jellyfin 12.4.0 behind Traefik: the Roku closed its
  poll on **Home** (Traefik logged 499), and the leave and the paused reap each fired 12 to 15 seconds
  later with `app closed its poll: True`.
- An app that never polls (HTTP, or one without the long poll) never counts as closed. On HTTP the
  server ends the session itself when the socket drops.
- The grace depends on the app retrying a failed poll within a few seconds (it retries after 1, 2
  and 4 seconds). A slower retry in a future app would make the plugin drop a live app.
- A reverse proxy or tunnel that restarts mid-poll looks the same as an app that exited: the server
  sees the request aborted. The app's retries fail at the proxy, with growing gaps, so an outage of
  more than about 7 seconds takes a live viewer out of the group, and they rejoin by hand. Accepted
  because the outage is rare and stalls playback anyway. If it is reported, the fix is in the app: retry
  a failed poll at a flat second for the first 10 seconds after a healthy poll.
- If a Roku or a proxy stops passing the close on, behavior falls back to the two-wait window, which
  is still correct, only slower.

## Alternatives considered

- **The two-wait window alone.** It acts 50 to 65 seconds after the last poll, so groups on 10.11 and
  12.0 could stall for about a minute.
- **A separate, shorter staleness threshold.** It leaves two liveness rules to keep in step, and a
  network blip could drop a live viewer from the group.
