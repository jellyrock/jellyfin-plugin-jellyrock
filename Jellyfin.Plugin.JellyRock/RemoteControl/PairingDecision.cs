using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The pure rules behind cold-launch pairing (issue #668) — freshness, advertisability, and the
/// upsert-with-prune that maintains the persisted pairing set. Time is injected (no
/// <see cref="DateTime.UtcNow"/> inside) so every rule is deterministic and unit-testable in isolation,
/// exactly like <see cref="ReapDecision"/>. All I/O (the reachability probe, config save) lives in the
/// service/store glue that calls these.
/// </summary>
public static class PairingDecision
{
    /// <summary>
    /// The window a pairing survives without the client re-reporting. JellyRock re-reports on every app
    /// open, so this only needs to exceed the longest gap between real uses of a device still in service;
    /// a device dark longer than this ages out and simply re-pairs on its next open. An internal constant,
    /// not a user setting: the behavior is self-correcting and invisible, so a dashboard knob would be
    /// config surface almost no admin should ever touch (promote to config only if real friction appears).
    /// </summary>
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// Whether a pairing last reported within the freshness window at <paramref name="now"/>. A future
    /// <paramref name="lastSeen"/> (clock skew) reads as fresh.
    /// </summary>
    /// <param name="lastSeen">When the pairing was last reported (UTC).</param>
    /// <param name="now">The reference time (UTC).</param>
    /// <param name="window">The freshness window.</param>
    /// <returns><c>true</c> if the pairing is still fresh.</returns>
    public static bool IsFresh(DateTime lastSeen, DateTime now, TimeSpan window)
    {
        return (now - lastSeen) <= window;
    }

    /// <summary>
    /// Whether a pairing may be advertised as a cast target: it must be <see cref="PairingRecord.Validated"/>
    /// (a reachable ECP address was found) AND still fresh. Liveness here is pairing-validity + freshness,
    /// NOT poll-freshness (a closed app never polls) — the crux of the #668 open/closed model. A live
    /// re-probe of current reachability is layered on at advertise time (a later phase); this rule is the
    /// persisted-state gate.
    /// </summary>
    /// <param name="record">The pairing to test.</param>
    /// <param name="now">The reference time (UTC).</param>
    /// <param name="window">The freshness window.</param>
    /// <returns><c>true</c> if the pairing is eligible to advertise.</returns>
    public static bool IsAdvertisable(PairingRecord record, DateTime now, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Validated && IsFresh(record.LastSeen, now, window);
    }

    /// <summary>
    /// Whether the phantom cast target should be advertised for a pairing <b>right now</b> (issue #668,
    /// P2). Three conditions, all required: the pairing is <see cref="IsAdvertisable(PairingRecord, DateTime, TimeSpan)"/>
    /// (validated + fresh, the persisted-state gate); the app is <b>not</b> currently open
    /// (<paramref name="appIsOpen"/> — an open app owns its own live session and full capabilities, so the
    /// manager must not stomp them with the reduced closed-state set); and a live advertise-time reachability
    /// re-probe still passes (<paramref name="reachableNow"/> — a Roku that has since powered off or left the
    /// LAN drops from the live cast list rather than lingering as an un-wakeable target). The two liveness
    /// signals are computed by the caller (session-manager state and an ECP probe) and injected, so this stays
    /// pure and deterministic like the rest of <see cref="PairingDecision"/>.
    /// </summary>
    /// <param name="record">The pairing to test.</param>
    /// <param name="appIsOpen">Whether a live (poll-fresh) JellyRock session already exists for this device.</param>
    /// <param name="reachableNow">Whether a live ECP re-probe of the validated wake address currently answers.</param>
    /// <param name="now">The reference time (UTC).</param>
    /// <param name="window">The freshness window.</param>
    /// <returns><c>true</c> if the phantom should be published/kept live for this pairing.</returns>
    public static bool ShouldPublish(PairingRecord record, bool appIsOpen, bool reachableNow, DateTime now, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !appIsOpen && reachableNow && IsAdvertisable(record, now, window);
    }

    /// <summary>
    /// Produce the new pairing set after a fresh report: any existing record for the same device is
    /// replaced by <paramref name="incoming"/>, records that have gone stale (past the window at
    /// <paramref name="now"/>) are pruned, and everything else is preserved in order. Prune-on-write keeps
    /// the persisted set from accumulating abandoned pairings; a pruned device simply re-pairs next open.
    /// Device matching is case-insensitive. Pure: returns a new list and mutates nothing.
    /// </summary>
    /// <param name="existing">The current pairing set.</param>
    /// <param name="incoming">The freshly reported pairing (its <see cref="PairingRecord.LastSeen"/> should be <paramref name="now"/>).</param>
    /// <param name="now">The reference time (UTC).</param>
    /// <param name="window">The freshness window used to prune stale records.</param>
    /// <returns>The new pairing set.</returns>
    public static IReadOnlyList<PairingRecord> Upsert(
        IReadOnlyList<PairingRecord> existing,
        PairingRecord incoming,
        DateTime now,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var result = new List<PairingRecord>();
        foreach (var record in existing)
        {
            // The incoming report supersedes any prior record for the same device.
            if (string.Equals(record.JellyfinDeviceId, incoming.JellyfinDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Reap abandoned pairings on write. Prune on staleness alone — a fresh-but-unvalidated
            // record (reported recently, probe failed) is kept; it documents a paired-but-unreachable
            // device and may validate on its next report.
            if (!IsFresh(record.LastSeen, now, window))
            {
                continue;
            }

            result.Add(record);
        }

        result.Add(incoming);
        return result;
    }
}
