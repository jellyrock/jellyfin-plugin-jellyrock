using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The cold-launch pairing report body a JellyRock client POSTs to <c>/JellyRock/RemoteControl/pair</c>
/// (issue #668). It carries only what the server cannot infer about the device: its LAN addresses and
/// its ECP app identity. Identity (the Jellyfin device + user) is NOT in the body — it is bound from
/// the request's authenticated session, so a hostile body can't claim another device's pairing.
/// </summary>
public sealed class PairRequest
{
    /// <summary>
    /// Gets the device's LAN IP addresses (the values of the client's <c>roDeviceInfo.GetIPAddrs()</c>).
    /// The server probes these to find the one it can reach for the ECP <c>/launch</c> wake.
    /// </summary>
    public IReadOnlyList<string>? RokuIps { get; init; }

    /// <summary>
    /// Gets the Roku ECP app id (<c>roAppInfo.GetID()</c>): <c>"dev"</c> for a sideloaded build, else
    /// the published channel id. Used as <c>/launch/&lt;appId&gt;</c> when waking the closed app.
    /// </summary>
    public string? AppId { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a sideloaded (developer) build (<c>roAppInfo.IsDev()</c>).
    /// Lets the server name a dev target distinctly from a store install on the same physical Roku.
    /// </summary>
    public bool IsDev { get; init; }
}
