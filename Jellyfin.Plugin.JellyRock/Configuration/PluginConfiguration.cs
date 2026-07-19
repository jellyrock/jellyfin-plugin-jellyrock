using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRock.Configuration;

/// <summary>
/// JellyRock Companion configuration. Its remote-control timings stay fixed (session matching is a
/// fixed client identifier; the liveness window derives from the client's own poll cadence — see
/// <see cref="RemoteControl.QueueingSessionController"/>). The two admin-editable switches below gate
/// cold-launch cast-target visibility (issue #668); they are surfaced on the plugin's dashboard config
/// page. It also persists the cold-launch pairing set: machine state populated by the client's pairing
/// reports, not settings an admin edits.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether closed Rokus are advertised as cold-launch cast targets
    /// (issue #668). Default <c>true</c>. When false, <see cref="RemoteControl.PhantomSessionService"/>
    /// publishes nothing and revokes any live phantom on its next tick, so only an <b>open</b> JellyRock
    /// appears as a cast target. The pairing store is left untouched, so flipping this back on restores
    /// targets on the next refresh with no re-pair wait. Admin-editable on the dashboard config page.
    /// </summary>
    public bool EnableColdLaunchCasting { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether sideloaded developer builds (<see cref="PairingRecord.IsDev"/>,
    /// shown as "JellyRock (dev)") are included as cold-launch cast targets. Default <c>true</c> so an
    /// accidental sideload still surfaces. When false, only published installs are advertised while closed —
    /// useful for test / CI Rokus whose dev builds are never a real cast destination. Has no effect unless
    /// <see cref="EnableColdLaunchCasting"/> is also true. Admin-editable on the dashboard config page.
    /// </summary>
    public bool IncludeDevelopmentBuilds { get; set; } = true;

    /// <summary>
    /// Gets the persisted cold-launch pairings — the validated Rokus the server can wake via ECP while
    /// JellyRock is closed. Populated by <c>/JellyRock/RemoteControl/pair</c> reports; not user-editable.
    /// Get-only collection so plugin-XML serialization populates it in place.
    /// <para><b>Server-owned machine state, not an admin setting.</b> Marked <see cref="JsonIgnoreAttribute"/>
    /// so it is excluded from the plugin's JSON config API: the dashboard config page neither receives nor
    /// can overwrite it. This is load-bearing — System.Text.Json cannot repopulate a get-only collection on
    /// deserialize, so without this a settings save would round-trip an <i>empty</i> Pairings and wipe every
    /// paired device. It is still persisted to the plugin XML (XmlSerializer does not honor the JSON
    /// attribute), and re-injected across admin saves by <see cref="Plugin.UpdateConfiguration"/>.</para>
    /// </summary>
    [JsonIgnore]
    public Collection<PairingRecord> Pairings { get; } = new();
}
