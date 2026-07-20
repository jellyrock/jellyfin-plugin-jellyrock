# Repo layout

```text
CHANGELOG.md                          # Keep a Changelog; release notes come from here
Directory.Build.props                 # assembly <Version> (kept in lockstep with build.yaml)
jellyfin.ruleset / .editorconfig      # analyzer + style config (StyleCop, etc.)
Jellyfin.Plugin.JellyRock.sln
scripts/                              # release helpers (set-version, changelog-extract, parity)
.github/workflows/                    # ci.yml, release-prepare.yml, release.yml
docs/
  features/                           # per-feature docs linked from the README
  dev/                                # sideload + this layout
Jellyfin.Plugin.JellyRock/
  build.yaml                          # plugin manifest (guid, targetAbi, framework, artifact, version); jprm reads this
  Jellyfin.Plugin.JellyRock.csproj    # net9.0, compiled against the Jellyfin 10.11.0 API floor
  Plugin.cs                           # BasePlugin<PluginConfiguration> + IHasWebPages (serves the config page);
                                      #   UpdateConfiguration override preserves Pairings across an admin save
  PluginServiceRegistrator.cs         # registers the hosted services + PairingValidationService into DI
  Configuration/
    PluginConfiguration.cs            # the two cold-cast admin toggles + the [JsonIgnore] Pairings machine state
    configPage.html                   # dashboard config page (embedded resource)
  RemoteControl/
    RemoteControlController.cs        # /JellyRock/RemoteControl long-poll + probe + /pair endpoints
    QueueingSessionController.cs      # ISessionController: queues commands + poll-liveness gating (remote control)
    JellyRockSessionService.cs        # attaches the controller + forces the capability (remote control)
    CommandEnvelope.cs                # the { MessageType, Data, MessageId } wire shape (remote control)
    PlaybackReaperService.cs          # sweeps + reaps idle JellyRock playback sessions (cleanup, jellyrock#43)
    ReapDecision.cs                   # pure reap-eligibility rule + resume-position correction (cleanup)
    PhantomSessionService.cs          # publishes/revokes the closed-app cast phantom (cold-launch cast, #668)
    PhantomSessionController.cs       # fires the ECP /launch wake for a phantom (cold-launch cast)
    PairingValidationService.cs       # ECP reachability probe — the LAN-local validation gate (cold-launch cast)
    PairingDecision.cs                # pure freshness / advertisability / config-gate rules (cold-launch cast)
    PairingStore.cs / PairingRecord.cs / PairRequest.cs   # pairing persistence + wire shapes (cold-launch cast)
Jellyfin.Plugin.JellyRock.Tests/      # xUnit tests (queue / liveness / serialization / reaper / pairing / config)
```
