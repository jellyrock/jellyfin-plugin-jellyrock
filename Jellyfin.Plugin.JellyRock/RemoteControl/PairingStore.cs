using System;
using System.Linq;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Thin persistence glue for cold-launch pairings (issue #668): serializes read-modify-write against the
/// plugin configuration and saves it. All judgment (upsert, prune, freshness) lives in the pure
/// <see cref="PairingDecision"/>; this type only holds the write lock and the config plumbing, so there
/// is nothing here to unit-test beyond what <see cref="PairingDecision"/> already covers.
/// </summary>
public static class PairingStore
{
    // Two clients pairing at once would each read-modify-write the same Pairings collection; serialize
    // so one can't clobber the other's record (mirrors JellyRockSessionService.AttachLock).
    private static readonly object WriteLock = new();

    /// <summary>
    /// Upsert a freshly reported pairing into the persisted set (pruning stale entries) and save the
    /// configuration. No-op if the plugin instance isn't available.
    /// </summary>
    /// <param name="record">The reported pairing (its <see cref="PairingRecord.LastSeen"/> should be <paramref name="now"/>).</param>
    /// <param name="now">The reference time (UTC).</param>
    public static void Upsert(PairingRecord record, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(record);

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        lock (WriteLock)
        {
            var config = plugin.Configuration;
            var updated = PairingDecision.Upsert(config.Pairings.ToList(), record, now, PairingDecision.FreshnessWindow);

            // Pairings is a get-only collection (plugin-XML serialization contract), so replace contents
            // in place rather than reassigning the property.
            config.Pairings.Clear();
            foreach (var pairing in updated)
            {
                config.Pairings.Add(pairing);
            }

            plugin.SaveConfiguration();
        }
    }
}
