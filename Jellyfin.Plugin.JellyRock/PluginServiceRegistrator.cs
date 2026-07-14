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
    }
}
