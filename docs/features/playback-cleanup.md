# Fast closed-app playback cleanup

Press **Home** on the Roku mid-playback and the app is torn down instantly, with no chance to tell the
server it stopped. ([JellyRock issue #43](https://github.com/jellyrock/jellyrock/issues/43))

On an **http** server this already resolves itself with no plugin, within seconds. The plugin **extends
the same fast cleanup to https** servers, where the close goes unsignaled. It clears the lingering
session, playing or paused, within seconds of the Roku closing its connection to the plugin, or within
about a minute otherwise, and records an accurate resume point.

## Why a plugin is needed on HTTPS

On an **http** server JellyRock holds Jellyfin's native session socket, so when the app closes the socket
drops and the server removes the session within seconds (this is what fixed #43 for http, via JellyRock's
`ws://` support). On an **https** server Roku can't open that socket (no `wss://`), so nothing signals the
close and Jellyfin keeps the transcode running with a phantom "now playing" until its own idle check reaps
it ~5-10 minutes later.

## How it works

A lightweight background sweep watches JellyRock sessions. It stops a session the same way Jellyfin's
own idle reaper does, on a faster clock, in either of two cases:

- **The app has closed.** JellyRock stopped polling the plugin, and it either closed its poll
  connection and opened no new one within a few seconds, or went two poll cycles without polling. This
  covers a paused video too.
- **The check-ins stopped.** The video is playing, but the client's playback check-ins (its
  `Sessions/Playing/Progress` reports, every 10 seconds) have gone silent past a 60-second threshold.

It records the resume point at the **last confirmed check-in** position rather than Jellyfin's
forward-extrapolated one, so resuming later lands where you actually stopped instead of skipping ahead.
Jellyfin 12.0 made its own idle cleanup do the same
([jellyfin#17631](https://github.com/jellyfin/jellyfin/pull/17631)), so on 12.0 and newer the resume
point is right either way, and the plugin's gain is the faster cleanup.
A paused session keeps its paused position as the resume point.

## Requirements

An **HTTPS** Jellyfin server (10.11+) and JellyRock **v1.15.0 or newer**, the release that moved
playback progress reporting to a ~10s cadence, which the cleanup relies on to detect a closed app safely.
On older clients the plugin does nothing and Jellyfin's default idle handling applies (unchanged).
