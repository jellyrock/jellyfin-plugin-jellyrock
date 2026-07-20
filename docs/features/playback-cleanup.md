# Fast closed-app playback cleanup (HTTPS)

Press **Home** on the Roku mid-playback and the app is torn down instantly, with no chance to tell the
server it stopped. This feature reaps the lingering session in **~60 seconds** and records an accurate
resume point. ([JellyRock issue #43](https://github.com/jellyrock/jellyrock/issues/43))

## Why a plugin is needed on HTTPS

On an **http** server this already resolves itself: JellyRock holds Jellyfin's native session socket, so
when the app closes the socket drops and the server removes the session within seconds (this is what
fixed #43 for http, via JellyRock's `ws://` support). On an **https** server Roku can't open that socket
(no `wss://`), so nothing signals the close and Jellyfin keeps the transcode running with a phantom "now
playing" until its own idle check reaps it ~5-10 minutes later.

## How it works

A lightweight background sweep watches JellyRock sessions. When one is actively playing but its playback
check-ins (the client's ~10s `Sessions/Playing/Progress` reports) have gone silent past a 60-second
threshold, it stops the session the same way Jellyfin's own idle reaper does — just on a faster clock.

It records the resume point at the **last confirmed check-in** position rather than Jellyfin's
forward-extrapolated one, so resuming later lands where you actually stopped instead of skipping ahead.
Paused sessions are left untouched (Jellyfin's own paused/idle handling covers those).

## Requirements

An **HTTPS** Jellyfin server (10.11+) and JellyRock **v1.15.0 or newer** — the release that moved
playback progress reporting to a ~10s cadence, which the cleanup relies on to detect a closed app safely.
On older clients the plugin does nothing and Jellyfin's default idle handling applies (unchanged).
