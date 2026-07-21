using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyRock;

/// <summary>
/// Registers the plugin's background services into the server DI container. Discovered automatically
/// by Jellyfin's PluginManager. (The <see cref="RemoteControl.RemoteControlController"/> endpoint is
/// discovered separately as an MVC ApplicationPart and needs no registration here.)
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<JellyRockSessionService>();
        serviceCollection.AddHostedService<PlaybackReaperService>();

        // Publishes the cold-launch phantom cast target for validated+reachable pairings while JellyRock
        // is closed (#668, P2). Reuses PairingValidationService for the advertise-time reachability re-probe.
        serviceCollection.AddHostedService<PhantomSessionService>();

        // Resolved by RemoteControlController (pairing reachability probe) and PhantomSessionService
        // (advertise-time re-probe) for the cold-launch producer (#668).
        serviceCollection.AddSingleton<PairingValidationService>();
    }
}
