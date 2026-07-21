using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.JellyRock.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyRock;

/// <summary>
/// JellyRock Companion — the server-side helper plugin for the JellyRock Roku client.
/// Its first feature (issue #667) makes JellyRock a remote-control ("Play On") target on an
/// HTTPS server, where Roku's lack of socket TLS rules out Jellyfin's native session WebSocket: the
/// plugin exposes an authenticated HTTP long-poll command channel JellyRock consumes over TLS.
/// Named for the companion role, not the feature, so future server-assisted features live here
/// without a rename. Its cold-launch cast-target visibility (issue #668) is admin-tunable via the
/// dashboard config page (see <see cref="GetPages"/>).
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
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

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    /// <summary>
    /// Re-injects the server-owned cold-launch pairings across an admin settings save so a config-page save
    /// can never wipe them (issue #668). The dashboard round-trips the whole <see cref="PluginConfiguration"/>,
    /// but <see cref="PluginConfiguration.Pairings"/> is machine state populated by <c>/pair</c> reports, not
    /// an admin setting — and System.Text.Json cannot repopulate its get-only collection on deserialize, so an
    /// incoming config always arrives with Pairings empty. Copy the current pairings into the incoming config
    /// before it is persisted; the toggles the admin actually changed are already on <paramref name="configuration"/>.
    /// </summary>
    /// <param name="configuration">The incoming configuration from the config page.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration is PluginConfiguration incoming && !ReferenceEquals(incoming, Configuration))
        {
            // Snapshot to avoid enumerating while a concurrent /pair report mutates the live collection.
            foreach (var pairing in Configuration.Pairings.ToList())
            {
                incoming.Pairings.Add(pairing);
            }
        }

        base.UpdateConfiguration(configuration);
    }
}
