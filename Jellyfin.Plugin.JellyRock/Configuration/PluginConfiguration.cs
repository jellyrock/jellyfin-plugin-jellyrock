using System.Collections.ObjectModel;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRock.Configuration;

/// <summary>
/// JellyRock Companion configuration. Has no user-tunable settings — session matching is a fixed client
/// identifier and the remote-control liveness window is derived from the client's own poll cadence
/// (see <see cref="RemoteControl.QueueingSessionController"/>). It does persist the cold-launch pairing
/// set (issue #668): machine state populated by the client's pairing reports, not settings an admin edits.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets the persisted cold-launch pairings — the validated Rokus the server can wake via ECP while
    /// JellyRock is closed. Populated by <c>/JellyRock/RemoteControl/pair</c> reports; not user-editable.
    /// Get-only collection so plugin-XML serialization populates it in place.
    /// </summary>
    public Collection<PairingRecord> Pairings { get; } = new();
}
