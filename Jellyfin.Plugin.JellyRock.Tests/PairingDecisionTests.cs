using System;
using System.Linq;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Unit tests for <see cref="PairingDecision"/> — the pure freshness / advertisability / upsert-with-prune
/// rules behind cold-launch pairing (JellyRock issue #668). Time is injected, so every case is deterministic.
/// </summary>
public class PairingDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private static PairingRecord Record(string deviceId, DateTime lastSeen, bool validated = true)
    {
        return new PairingRecord
        {
            JellyfinDeviceId = deviceId,
            UserId = "u1",
            AppId = "dev",
            WakeIp = "192.168.1.5",
            Validated = validated,
            LastValidated = validated ? lastSeen : default,
            LastSeen = lastSeen
        };
    }

    [Fact]
    public void IsFresh_WithinWindow_True()
    {
        Assert.True(PairingDecision.IsFresh(Now.AddDays(-13), Now, Window));
    }

    [Fact]
    public void IsFresh_ExactlyAtWindow_True()
    {
        // Inclusive at the boundary: a device seen exactly N days ago is still fresh.
        Assert.True(PairingDecision.IsFresh(Now.AddDays(-14), Now, Window));
    }

    [Fact]
    public void IsFresh_PastWindow_False()
    {
        Assert.False(PairingDecision.IsFresh(Now.AddDays(-15), Now, Window));
    }

    [Fact]
    public void IsFresh_FutureLastSeen_True()
    {
        // Clock skew: a lastSeen slightly in the future must not read as stale.
        Assert.True(PairingDecision.IsFresh(Now.AddMinutes(5), Now, Window));
    }

    [Fact]
    public void IsAdvertisable_ValidatedAndFresh_True()
    {
        Assert.True(PairingDecision.IsAdvertisable(Record("d1", Now.AddDays(-1)), Now, Window));
    }

    [Fact]
    public void IsAdvertisable_FreshButNotValidated_False()
    {
        Assert.False(PairingDecision.IsAdvertisable(Record("d1", Now.AddDays(-1), validated: false), Now, Window));
    }

    [Fact]
    public void IsAdvertisable_ValidatedButStale_False()
    {
        Assert.False(PairingDecision.IsAdvertisable(Record("d1", Now.AddDays(-30)), Now, Window));
    }

    [Fact]
    public void ShouldPublish_ClosedValidatedFreshReachable_True()
    {
        // The nominal cold-cast case: app closed, pairing validated + fresh, Roku still answers ECP.
        Assert.True(PairingDecision.ShouldPublish(Record("d1", Now.AddDays(-1)), appIsOpen: false, reachableNow: true, Now, Window));
    }

    [Fact]
    public void ShouldPublish_AppOpen_False()
    {
        // Open app owns its own live session + full capabilities; the manager must not publish the
        // reduced closed-state phantom over it.
        Assert.False(PairingDecision.ShouldPublish(Record("d1", Now.AddDays(-1)), appIsOpen: true, reachableNow: true, Now, Window));
    }

    [Fact]
    public void ShouldPublish_UnreachableNow_False()
    {
        // Validated + fresh in the store, but the live re-probe failed (Roku powered off / left the LAN):
        // it must drop from the cast list rather than linger as an un-wakeable target.
        Assert.False(PairingDecision.ShouldPublish(Record("d1", Now.AddDays(-1)), appIsOpen: false, reachableNow: false, Now, Window));
    }

    [Fact]
    public void ShouldPublish_NotValidated_False()
    {
        Assert.False(PairingDecision.ShouldPublish(Record("d1", Now.AddDays(-1), validated: false), appIsOpen: false, reachableNow: true, Now, Window));
    }

    [Fact]
    public void ShouldPublish_Stale_False()
    {
        Assert.False(PairingDecision.ShouldPublish(Record("d1", Now.AddDays(-30)), appIsOpen: false, reachableNow: true, Now, Window));
    }

    [Fact]
    public void Upsert_NewDevice_Appended()
    {
        var existing = new[] { Record("d1", Now.AddDays(-1)) };
        var incoming = Record("d2", Now);

        var result = PairingDecision.Upsert(existing, incoming, Now, Window);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.JellyfinDeviceId == "d1");
        Assert.Contains(result, r => r.JellyfinDeviceId == "d2");
    }

    [Fact]
    public void Upsert_SameDevice_ReplacedNotDuplicated()
    {
        var existing = new[] { Record("d1", Now.AddDays(-5)) };
        var incoming = Record("d1", Now);
        incoming.WakeIp = "10.0.0.9";

        var result = PairingDecision.Upsert(existing, incoming, Now, Window);

        var only = Assert.Single(result);
        Assert.Equal("10.0.0.9", only.WakeIp);
        Assert.Equal(Now, only.LastSeen);
    }

    [Fact]
    public void Upsert_SameDeviceCaseInsensitive_Replaced()
    {
        var existing = new[] { Record("DEVICE-ABC", Now.AddDays(-5)) };
        var incoming = Record("device-abc", Now);

        var result = PairingDecision.Upsert(existing, incoming, Now, Window);

        Assert.Single(result);
    }

    [Fact]
    public void Upsert_PrunesStaleOtherDevices()
    {
        var existing = new[]
        {
            Record("fresh", Now.AddDays(-2)),
            Record("stale", Now.AddDays(-40)),
        };
        var incoming = Record("new", Now);

        var result = PairingDecision.Upsert(existing, incoming, Now, Window);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.JellyfinDeviceId == "fresh");
        Assert.Contains(result, r => r.JellyfinDeviceId == "new");
        Assert.DoesNotContain(result, r => r.JellyfinDeviceId == "stale");
    }

    [Fact]
    public void Upsert_KeepsFreshButUnvalidatedRecord()
    {
        // A recently-reported but unreachable device documents a paired-but-unvalidated Roku; only
        // staleness prunes, not validation state.
        var existing = new[] { Record("unvalidated", Now.AddDays(-1), validated: false) };
        var incoming = Record("new", Now);

        var result = PairingDecision.Upsert(existing, incoming, Now, Window);

        Assert.Contains(result, r => r.JellyfinDeviceId == "unvalidated");
    }

    [Fact]
    public void Upsert_PreservesOrderOfSurvivors()
    {
        var existing = new[]
        {
            Record("a", Now.AddDays(-1)),
            Record("b", Now.AddDays(-2)),
        };
        var incoming = Record("c", Now);

        var result = PairingDecision.Upsert(existing, incoming, Now, Window).ToList();

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(r => r.JellyfinDeviceId).ToArray());
    }
}
