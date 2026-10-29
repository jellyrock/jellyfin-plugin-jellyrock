# SyncPlay on HTTPS

JellyRock joins Jellyfin SyncPlay groups (watch together) from **v3.3.0**. On an **https** server it
needs this plugin to do that. On an **http** server it needs nothing else.
([JellyRock issue #1251](https://github.com/jellyrock/jellyrock/issues/1251))

Without the plugin on an https server, JellyRock shows its **SyncPlay** icon disabled. Selecting it
explains that the server needs the JellyRock plugin. You don't need to restart JellyRock after you
install the plugin: the app checks for it again every few minutes.

## Why a plugin is needed on HTTPS

Jellyfin sends every SyncPlay message (the group's play, pause and seek commands, and who joined or
left) over the session **WebSocket**. Roku has no socket TLS (no `wss://`), so on an https server
JellyRock receives those messages through the plugin's long poll, the same channel
["Play On" remote control](remote-control.md) uses. The plugin writes each message exactly as the
server's socket would, so JellyRock reads both transports the same way.

## Leaving the group when JellyRock closes

Pressing **Home** on the Roku closes JellyRock at once, so the app can't tell the group it left. On
http the session socket drops and Jellyfin takes the Roku out of the group within seconds. On https
nothing drops, so the group keeps a member that never answers. The others then wait on it at every
seek or change of video, and on servers before 12.1 they wait until it is gone.

The plugin closes that gap. When the Roku closes its poll connection and opens no new one within a
few seconds, the plugin takes it out of its group. When the connection goes quiet instead (for
example, the Roku loses its network), the plugin waits until the Roku has gone two poll cycles without
polling, which is under a minute. Either way it leaves only the group: the Roku stays available for
[cold-launch cast](cold-launch-cast.md).

JellyRock also leaves its last group the next time it starts, in case the server still lists it there.

## Requirements

An **HTTPS** Jellyfin server (10.11+) and JellyRock **v3.3.0 or newer**.
