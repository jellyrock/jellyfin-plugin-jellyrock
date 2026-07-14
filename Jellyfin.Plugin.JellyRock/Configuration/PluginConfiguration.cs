using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyRock.Configuration;

/// <summary>
/// JellyRock Companion configuration — intentionally empty. The plugin has no user-tunable settings:
/// session matching is a fixed client identifier and the liveness window is derived from the client's
/// own poll cadence (see <see cref="RemoteControl.QueueingSessionController"/>). The type exists only
/// because <see cref="MediaBrowser.Common.Plugins.BasePlugin{TConfigurationType}"/> requires one.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
}
