# Cold-launch cast

Cast to a Roku **even when JellyRock is closed**. The plugin advertises the closed device as a "Play On"
target in Jellyfin's web and mobile apps; selecting it wakes the Roku over ECP and launches JellyRock
straight into the item you cast. Once the app is open, control hands off to the live remote-control
channel, so this feature's job is strictly the closed-to-open bridge.

Unlike the other two features, cold-launch cast works on **http and https** servers alike — the wake is
an ECP call, independent of the remote-control transport.

## How it works

1. **Pairing.** On each app open, JellyRock reports its wake identity (LAN addresses, ECP app id, dev
   flag) to the plugin's authenticated `POST /JellyRock/RemoteControl/pair` endpoint. Identity is bound
   from the auth claim, never the request body.
2. **Validation gate.** The plugin probes the reported addresses over the ungated ECP
   `GET /query/device-info`. Only a device the server can actually reach on the LAN validates and is
   stored; a remote/cloud server, a reverse-proxied session, or a powered-dark device never validates,
   so it is never advertised. This makes "LAN-local only" self-enforcing rather than a heuristic.
3. **Phantom session.** A background worker publishes a **phantom** `SessionInfo` for each validated,
   reachable, closed device, keyed on the same Jellyfin `DeviceId` the open app uses. It advertises a
   reduced capability set so the web renders only the wake affordance, and re-probes reachability every
   ~30s so a Roku that powers off drops from the cast list within a tick.
4. **Wake.** Selecting the phantom fires ECP `POST /launch/<appId>` with the deep-link `contentId`,
   cold-starting JellyRock into the item. The live session then takes over and the phantom steps aside.

Full architecture — the phantom-session model, the validation gate, the net9/10.11 floor, and the
3rd-party-ECP policy caveat — is recorded in
[ADR 0023](https://github.com/jellyrock/jellyrock/blob/main/docs/adr/0023-cold-launch-cast-producer.md).

## Settings

Two admin toggles under **Dashboard → Plugins → JellyRock Companion**, both **on by default**:

- **Show closed devices as cast targets** — the master switch. When enabled, a Roku with JellyRock
  installed appears in "Play On" even while the app is closed. When disabled, only Rokus with JellyRock
  already open appear.
- **Include development builds** — when enabled, sideloaded builds (shown as "JellyRock (dev)") also
  appear as closed cast targets. Turn it off if you run test or CI devices whose dev builds you never
  cast to; published installs still appear. Casting to a dev build while it is open still works either
  way.

Changes take up to ~30 seconds to take effect (the phantom refresh cadence). Toggling is lossless — the
paired-device list is preserved across saves, so turning a toggle back on restores targets on the next
refresh with no re-pair.

## Requirements

Jellyfin **10.11 or newer** and JellyRock **v2.23.0 or newer**, with the server sharing your Roku's LAN.
