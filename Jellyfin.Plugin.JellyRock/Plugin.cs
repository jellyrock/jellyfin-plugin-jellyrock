using System;
using Jellyfin.Plugin.JellyRock.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyRock;

/// <summary>
/// JellyRock Companion — the server-side helper plugin for the JellyRock Roku client.
/// Its first feature (issue #667) makes JellyRock a remote-control ("Play On") target on an
/// HTTPS server, where Roku's lack of socket TLS rules out Jellyfin's native session WebSocket: the
/// plugin exposes an authenticated HTTP long-poll command channel JellyRock consumes over TLS.
/// Named for the companion role, not the feature, so future server-assisted features live here
/// without a rename. It has no settings — the one timing parameter is derived from the client's poll.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellyRock Companion";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("6c4f4b7e-50f7-43b8-a180-7de26f9033d6");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
}
