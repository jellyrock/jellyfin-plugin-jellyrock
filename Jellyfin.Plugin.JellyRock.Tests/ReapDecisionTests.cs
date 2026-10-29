using System;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Unit tests for <see cref="ReapDecision"/> — the pure eligibility rule (including the client-version
/// gate) and the resume-position correction that carry all the judgment behind the idle-playback reaper
/// (JellyRock issue #43).
/// </summary>
public class ReapDecisionTests
{
    private const int ThresholdSeconds = 60;

    // A version at/after the v1.15.0 cutoff where JellyRock reports playback progress every ~10s.
    private const string CurrentVersion = "2.23.0";

    private static readonly DateTime Now = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string JellyRock = JellyRockSessionService.JellyRockClientName;

    [Fact]
    public void IsReapable_StalePlayingJellyRockSession_True()
    {
        Assert.True(ReapDecision.IsReapable(
            JellyRock, CurrentVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-61), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_NonJellyRockClient_False()
    {
        Assert.False(ReapDecision.IsReapable(
            "Jellyfin Web", CurrentVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-300), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_NoNowPlaying_False()
    {
        Assert.False(ReapDecision.IsReapable(
            JellyRock, CurrentVersion, hasNowPlaying: false, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-300), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_Paused_False()
    {
        // Paused sessions stop sending check-ins, so they look stale; excluded to avoid reaping a
        // paused-but-alive app (left to Jellyfin's own paused/idle handling).
        Assert.False(ReapDecision.IsReapable(
            JellyRock, CurrentVersion, hasNowPlaying: true, isPaused: true,
            lastPlaybackCheckIn: Now.AddSeconds(-300), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_FreshCheckIn_False()
    {
        Assert.False(ReapDecision.IsReapable(
            JellyRock, CurrentVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-30), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_ExactlyAtThreshold_False()
    {
        // Strictly greater-than: at exactly the threshold the session is not yet reaped.
        Assert.False(ReapDecision.IsReapable(
            JellyRock, CurrentVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-ThresholdSeconds), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_ClientMatchIsCaseInsensitive_True()
    {
        Assert.True(ReapDecision.IsReapable(
            "jellyrock", CurrentVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-90), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_OldClientBelowReportCadenceCutoff_False()
    {
        // v1.14.0 reports every ~30s, so the 60s threshold has too little margin — excluded, left to
        // Jellyfin's native idle reap (their behavior today, no regression).
        Assert.False(ReapDecision.IsReapable(
            JellyRock, "1.14.0", hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-90), Now, ThresholdSeconds));
    }

    [Fact]
    public void IsReapable_ExactlyAtReportCadenceCutoff_True()
    {
        // v1.15.0 is the first version with ~10s reports — eligible.
        Assert.True(ReapDecision.IsReapable(
            JellyRock, "1.15.0", hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-90), Now, ThresholdSeconds));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void IsReapable_UnparseableVersion_False(string? appVersion)
    {
        // Fail safe toward not reaping when we can't establish the report cadence.
        Assert.False(ReapDecision.IsReapable(
            JellyRock, appVersion, hasNowPlaying: true, isPaused: false,
            lastPlaybackCheckIn: Now.AddSeconds(-300), Now, ThresholdSeconds));
    }

    [Fact]
    public void CorrectedPositionTicks_BacksOutTheDrift()
    {
        // Last real check-in was 90s ago at position 600s; the auto-progress timer has since advanced
        // PositionTicks to 690s. The correction must revert to the confirmed 600s.
        var position690 = TimeSpan.FromSeconds(690).Ticks;
        var checkIn = Now.AddSeconds(-90);

        var corrected = ReapDecision.CorrectedPositionTicks(position690, checkIn, Now, runtimeTicks: null);

        Assert.Equal(TimeSpan.FromSeconds(600).Ticks, corrected);
    }

    [Fact]
    public void CorrectedPositionTicks_ZeroDrift_ReturnsPosition()
    {
        var position = TimeSpan.FromSeconds(120).Ticks;

        var corrected = ReapDecision.CorrectedPositionTicks(position, Now, Now, runtimeTicks: null);

        Assert.Equal(position, corrected);
    }

    [Fact]
    public void CorrectedPositionTicks_DriftExceedsPosition_ClampsToZero()
    {
        // Near the very start: drift larger than the elapsed position would go negative -> clamp to 0.
        var position = TimeSpan.FromSeconds(5).Ticks;
        var checkIn = Now.AddSeconds(-60);

        var corrected = ReapDecision.CorrectedPositionTicks(position, checkIn, Now, runtimeTicks: null);

        Assert.Equal(0, corrected);
    }

    [Fact]
    public void CorrectedPositionTicks_ClampsToRuntime()
    {
        // Near the end the auto-timer pins PositionTicks at the runtime; the correction must not exceed it.
        var runtime = TimeSpan.FromSeconds(1000).Ticks;
        var position = TimeSpan.FromSeconds(2000).Ticks; // absurd; forces the upper clamp
        var checkIn = Now; // zero drift, so corrected == position before clamping

        var corrected = ReapDecision.CorrectedPositionTicks(position, checkIn, Now, runtime);

        Assert.Equal(runtime, corrected);
    }

    [Theory]
    [InlineData(true, true, false, true)] // polled, then closed: reap
    [InlineData(false, true, false, false)] // never polled (HTTP, or no long poll): never read as closed
    [InlineData(true, true, true, false)] // app open
    [InlineData(true, false, false, false)] // nothing playing: nothing to reap
    public void IsClosedAppReapable_FollowsThePollAndNowPlaying(bool hasPolled, bool hasNowPlaying, bool appOpen, bool expected)
    {
        Assert.Equal(expected, ReapDecision.IsClosedAppReapable(JellyRockSessionService.JellyRockClientName, hasNowPlaying, hasPolled, appOpen));
    }

    [Fact]
    public void IsClosedAppReapable_NonJellyRockClient_False()
    {
        Assert.False(ReapDecision.IsClosedAppReapable("Jellyfin Web", hasNowPlaying: true, hasPolled: true, appOpen: false));
    }

    [Fact]
    public void ReapPositionTicks_Paused_KeepsThePausedPosition()
    {
        // The auto-progress timer does not advance a paused session, so there is no drift to back out,
        // however long ago the last check-in was.
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var ticks = ReapDecision.ReapPositionTicks(
            isPaused: true,
            positionTicks: TimeSpan.FromSeconds(600).Ticks,
            lastPlaybackCheckIn: now.AddMinutes(-3),
            nowUtc: now,
            runtimeTicks: TimeSpan.FromMinutes(90).Ticks);

        Assert.Equal(TimeSpan.FromSeconds(600).Ticks, ticks);
    }

    [Fact]
    public void ReapPositionTicks_Playing_BacksOutTheDrift()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var ticks = ReapDecision.ReapPositionTicks(
            isPaused: false,
            positionTicks: TimeSpan.FromSeconds(690).Ticks,
            lastPlaybackCheckIn: now.AddSeconds(-90),
            nowUtc: now,
            runtimeTicks: TimeSpan.FromMinutes(90).Ticks);

        Assert.Equal(TimeSpan.FromSeconds(600).Ticks, ticks);
    }
}
