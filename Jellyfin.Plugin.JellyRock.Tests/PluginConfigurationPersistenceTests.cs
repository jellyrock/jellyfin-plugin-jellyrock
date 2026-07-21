using System;
using System.IO;
using Jellyfin.Plugin.JellyRock.Configuration;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Regression tests for the admin-config save path (issue #668). The dashboard config page round-trips the
/// whole <see cref="PluginConfiguration"/> through Jellyfin's JSON API, and System.Text.Json cannot
/// repopulate the get-only <see cref="PluginConfiguration.Pairings"/> collection on deserialize — so an
/// incoming admin save always arrives with Pairings empty. <see cref="Plugin.UpdateConfiguration"/> must
/// re-inject the server-owned pairings so a settings save never wipes the paired-device machine state.
/// (Before the fix, saving the toggles wiped every pairing, orphaning any published cast phantom.)
/// </summary>
public class PluginConfigurationPersistenceTests
{
    private static Plugin CreatePlugin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "jrtest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginsPath.Returns(tempDir);
        paths.PluginConfigurationsPath.Returns(tempDir);
        // SerializeToFile is a no-op mock (SaveConfiguration touches no real file); DeserializeFromFile
        // returns a fresh default config so the lazy Configuration load yields a real object, not null.
        var xml = Substitute.For<IXmlSerializer>();
        xml.DeserializeFromFile(Arg.Any<Type>(), Arg.Any<string>()).Returns(_ => new PluginConfiguration());
        return new Plugin(paths, xml);
    }

    [Fact]
    public void UpdateConfiguration_IncomingEmptyPairings_PreservesExistingPairings()
    {
        var plugin = CreatePlugin();
        plugin.Configuration.Pairings.Add(new PairingRecord { JellyfinDeviceId = "dev-1", WakeIp = "192.168.1.5" });
        plugin.Configuration.Pairings.Add(new PairingRecord { JellyfinDeviceId = "dev-2", WakeIp = "192.168.1.6" });

        // Mirrors what the config page + System.Text.Json produce: toggles set, Pairings empty.
        var incoming = new PluginConfiguration { EnableColdLaunchCasting = false, IncludeDevelopmentBuilds = false };
        Assert.Empty(incoming.Pairings);

        plugin.UpdateConfiguration(incoming);

        // The admin's toggle change is applied...
        Assert.False(plugin.Configuration.EnableColdLaunchCasting);
        Assert.False(plugin.Configuration.IncludeDevelopmentBuilds);
        // ...and the server-owned pairings survived the save.
        Assert.Equal(2, plugin.Configuration.Pairings.Count);
        Assert.Contains(plugin.Configuration.Pairings, p => p.JellyfinDeviceId == "dev-1");
        Assert.Contains(plugin.Configuration.Pairings, p => p.JellyfinDeviceId == "dev-2");
    }

    [Fact]
    public void UpdateConfiguration_NoExistingPairings_LeavesIncomingEmpty()
    {
        var plugin = CreatePlugin();
        Assert.Empty(plugin.Configuration.Pairings);

        plugin.UpdateConfiguration(new PluginConfiguration { EnableColdLaunchCasting = false });

        Assert.False(plugin.Configuration.EnableColdLaunchCasting);
        Assert.Empty(plugin.Configuration.Pairings);
    }

    [Fact]
    public void Configuration_Defaults_BothTogglesOn()
    {
        var plugin = CreatePlugin();
        Assert.True(plugin.Configuration.EnableColdLaunchCasting);
        Assert.True(plugin.Configuration.IncludeDevelopmentBuilds);
    }
}
