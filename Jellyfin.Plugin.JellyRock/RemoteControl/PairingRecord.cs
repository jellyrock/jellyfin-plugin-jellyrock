using System;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// One persisted cold-launch pairing (issue #668) — a validated Roku the server can wake into content
/// via ECP <c>/launch</c> while JellyRock is closed. Keyed by the Jellyfin <see cref="JellyfinDeviceId"/>
/// so a device's open (live session) and closed (this record) states are one continuous identity.
///
/// <para>Only the single reachable <see cref="WakeIp"/> is persisted, not the client's whole reported
/// address list: the list is transient (used once to pick a reachable address) and re-sent on every
/// report, so there is nothing to gain from storing it. Serialized into
/// <see cref="Configuration.PluginConfiguration"/> as plugin XML — hence the parameterless ctor and
/// settable scalar properties.</para>
/// </summary>
public sealed class PairingRecord
{
    /// <summary>Gets or sets the Jellyfin device id (the pairing key; bound from the authenticated session, never the report body).</summary>
    public string JellyfinDeviceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin user id (N-format guid) that owns the session — the user who will see this device as a cast target.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Roku ECP app id for the <c>/launch/&lt;appId&gt;</c> wake (<c>"dev"</c> when sideloaded, else the channel id).</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this is a sideloaded developer build (lets the target be named distinctly).</summary>
    public bool IsDev { get; set; }

    /// <summary>Gets or sets the remote endpoint the server saw the report arrive from (diagnostic; the reachability probe is the actual gate).</summary>
    public string RemoteEndPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the reachable LAN address the server validated for the ECP wake; empty when no reported address answered.</summary>
    public string WakeIp { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the last report found a reachable ECP address (the validation gate). Only a validated pairing may be advertised.</summary>
    public bool Validated { get; set; }

    /// <summary>Gets or sets the UTC time the reachability probe last succeeded (default when never validated).</summary>
    public DateTime LastValidated { get; set; }

    /// <summary>Gets or sets the UTC time this pairing was last reported. Refreshed on every report; drives the freshness reap.</summary>
    public DateTime LastSeen { get; set; }
}
