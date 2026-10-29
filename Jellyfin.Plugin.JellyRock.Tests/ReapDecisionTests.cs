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

    [Fact]
    public void Decide_StalePlayingAppThatNeverPolled_CheckInsStale()
    {
        Assert.Equal(ReapReason.CheckInsStale, Decide(checkInSecondsAgo: 61));
    }

    [Theory]
    [InlineData(AppLiveness.Unknown)]
    [InlineData(AppLiveness.Closed)]
    public void Decide_NonJellyRockClient_None(AppLiveness liveness)
    {
        Assert.Equal(ReapReason.None, Decide(client: "Jellyfin Web", liveness: liveness));
    }

    [Theory]
    [InlineData(AppLiveness.Unknown)]
    [InlineData(AppLiveness.Closed)]
    public void Decide_NoNowPlaying_None(AppLiveness liveness)
    {
        Assert.Equal(ReapReason.None, Decide(hasNowPlaying: false, liveness: liveness));
    }

    [Fact]
    public void Decide_PausedAppThatNeverPolled_None()
    {
        // Paused sessions stop sending check-ins, so they look stale; with no poll to say the app closed,
        // a paused session is left to Jellyfin's own idle check.
        Assert.Equal(ReapReason.None, Decide(isPaused: true));
    }

    [Fact]
    public void Decide_OpenApp_None()
    {
        // An open app is alive whatever its check-ins say (a paused SyncPlay session can keep
        // IsPaused=false while check-ins go stale).
        Assert.Equal(ReapReason.None, Decide(liveness: AppLiveness.Open));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decide_ClosedApp_AppClosed_PlayingOrPaused_WithoutWaitingForCheckIns(bool isPaused)
    {
        Assert.Equal(ReapReason.AppClosed, Decide(isPaused: isPaused, liveness: AppLiveness.Closed, checkInSecondsAgo: 15));
    }

    [Fact]
    public void Decide_FreshCheckIn_None()
    {
        Assert.Equal(ReapReason.None, Decide(checkInSecondsAgo: 30));
    }

    [Fact]
    public void Decide_ExactlyAtThreshold_None()
    {
        // Strictly greater-than: at exactly the threshold the session is not yet reaped.
        Assert.Equal(ReapReason.None, Decide(checkInSecondsAgo: ThresholdSeconds));
    }

    [Theory]
    [InlineData(AppLiveness.Unknown, ReapReason.CheckInsStale)]
    [InlineData(AppLiveness.Closed, ReapReason.AppClosed)]
    public void Decide_ClientMatchIsCaseInsensitive(AppLiveness liveness, ReapReason expected)
    {
        Assert.Equal(expected, Decide(client: "jellyrock", liveness: liveness, checkInSecondsAgo: 90));
    }

    [Fact]
    public void Decide_OldClientBelowReportCadenceCutoff_None()
    {
        // v1.14.0 reports every ~30s, so the 60s threshold has too little margin — excluded, left to
        // Jellyfin's native idle reap (their behavior today, no regression).
        Assert.Equal(ReapReason.None, Decide(appVersion: "1.14.0", checkInSecondsAgo: 90));
    }

    [Fact]
    public void Decide_ExactlyAtReportCadenceCutoff_CheckInsStale()
    {
        // v1.15.0 is the first version with ~10s reports — eligible.
        Assert.Equal(ReapReason.CheckInsStale, Decide(appVersion: "1.15.0", checkInSecondsAgo: 90));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Decide_UnparseableVersion_None(string? appVersion)
    {
        // Fail safe toward not reaping when we can't establish the report cadence.
        Assert.Equal(ReapReason.None, Decide(appVersion: appVersion));
    }

    [Theory]
    [InlineData(false, false, AppLiveness.Unknown)] // never polled (HTTP, or no long poll): never read as closed
    [InlineData(false, true, AppLiveness.Open)] // the server's socket is open (HTTP)
    [InlineData(true, true, AppLiveness.Open)] // the poll is fresh
    [InlineData(true, false, AppLiveness.Closed)] // polled, then went quiet
    public void ReadLiveness_FollowsThePollAndTheOpenControllers(bool hasPolled, bool appOpen, AppLiveness expected)
    {
        Assert.Equal(expected, ReapDecision.ReadLiveness(hasPolled, appOpen));
    }

    [Theory]
    [InlineData(AppLiveness.Closed, true)]
    [InlineData(AppLiveness.Open, false)]
    [InlineData(AppLiveness.Unknown, false)]
    public void ShouldLeaveSyncPlayGroup_OnlyAClosedAppLeaves(AppLiveness liveness, bool expected)
    {
        Assert.Equal(expected, ReapDecision.ShouldLeaveSyncPlayGroup(JellyRockSessionService.JellyRockClientName, inSyncPlayGroup: true, liveness));
    }

    [Fact]
    public void ShouldLeaveSyncPlayGroup_NotInAGroup_False()
    {
        Assert.False(ReapDecision.ShouldLeaveSyncPlayGroup(JellyRockSessionService.JellyRockClientName, inSyncPlayGroup: false, AppLiveness.Closed));
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

    private static ReapReason Decide(
        string client = JellyRockSessionService.JellyRockClientName,
        string? appVersion = CurrentVersion,
        bool hasNowPlaying = true,
        bool isPaused = false,
        AppLiveness liveness = AppLiveness.Unknown,
        int checkInSecondsAgo = 300) =>
        ReapDecision.Decide(client, appVersion, hasNowPlaying, isPaused, liveness, Now.AddSeconds(-checkInSecondsAgo), Now, ThresholdSeconds);
}
