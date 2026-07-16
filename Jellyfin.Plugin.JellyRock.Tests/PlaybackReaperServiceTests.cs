using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Tests for <see cref="PlaybackReaperService"/> — that one sweep maps a stale JellyRock playback
/// session onto the right <see cref="ISessionManager.OnPlaybackStopped"/> call (with the corrected
/// resume position) and leaves healthy sessions untouched. Drives <c>SweepAsync</c> directly so the
/// wiring is verified without the timer. The eligibility/position judgment itself is covered by
/// <see cref="ReapDecisionTests"/>.
/// </summary>
public class PlaybackReaperServiceTests
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();

    [Fact]
    public async Task SweepAsync_StalePlayingJellyRockSession_ReapsWithCorrectedPosition()
    {
        // Last real check-in 90s ago; the auto-progress timer has since advanced PositionTicks to 690s.
        var session = BuildSession(
            client: JellyRockSessionService.JellyRockClientName,
            checkInSecondsAgo: 90,
            isPaused: false,
            positionSeconds: 690);
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        // Reaped once, with the drift backed out (690s advanced - ~90s drift ≈ 600s confirmed position).
        // A small window absorbs the real elapsed time between building the session and the sweep; the
        // exact tick math is pinned deterministically in ReapDecisionTests. The point here is that the
        // position was de-extrapolated to ~600s, not left at the raw auto-advanced 690s.
        await _sessionManager.Received(1).OnPlaybackStopped(Arg.Is<PlaybackStopInfo>(i =>
            i.SessionId == "sess-1"
            && i.PositionTicks >= TimeSpan.FromSeconds(598).Ticks
            && i.PositionTicks <= TimeSpan.FromSeconds(600).Ticks));
    }

    [Fact]
    public async Task SweepAsync_FreshSession_DoesNotReap()
    {
        var session = BuildSession(
            client: JellyRockSessionService.JellyRockClientName,
            checkInSecondsAgo: 20,
            isPaused: false,
            positionSeconds: 300);
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        await _sessionManager.DidNotReceive().OnPlaybackStopped(Arg.Any<PlaybackStopInfo>());
    }

    [Fact]
    public async Task SweepAsync_PausedSession_DoesNotReap()
    {
        var session = BuildSession(
            client: JellyRockSessionService.JellyRockClientName,
            checkInSecondsAgo: 300,
            isPaused: true,
            positionSeconds: 300);
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        await _sessionManager.DidNotReceive().OnPlaybackStopped(Arg.Any<PlaybackStopInfo>());
    }

    [Fact]
    public async Task SweepAsync_OldClientVersion_DoesNotReap()
    {
        // v1.14.0 reports every ~30s — below the 10s-cadence cutoff, so the plugin leaves it to
        // Jellyfin's native idle reap rather than risk reaping a still-playing session.
        var session = BuildSession(
            client: JellyRockSessionService.JellyRockClientName,
            checkInSecondsAgo: 300,
            isPaused: false,
            positionSeconds: 300,
            appVersion: "1.14.0");
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        await _sessionManager.DidNotReceive().OnPlaybackStopped(Arg.Any<PlaybackStopInfo>());
    }

    [Fact]
    public async Task SweepAsync_ReapedSession_MapsEveryStopInfoField()
    {
        // Guards the full PlaybackStopInfo mapping, not just SessionId/PositionTicks: a future refactor
        // that drops Item, ItemId, or MediaSourceId would silently break resume/cleanup, so pin them all.
        var session = BuildSession(
            client: JellyRockSessionService.JellyRockClientName,
            checkInSecondsAgo: 90,
            isPaused: false,
            positionSeconds: 690,
            id: "sess-map");
        var expectedItem = session.NowPlayingItem;
        var expectedItemId = session.NowPlayingItem!.Id;
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        await _sessionManager.Received(1).OnPlaybackStopped(Arg.Is<PlaybackStopInfo>(i =>
            i.SessionId == "sess-map"
            && ReferenceEquals(i.Item, expectedItem)
            && i.ItemId == expectedItemId
            && i.MediaSourceId == "ms-1"));
    }

    [Fact]
    public async Task SweepAsync_MixedSessions_ReapsOnlyTheStaleJellyRockSession()
    {
        // A realistic sweep: one reapable session amid healthy/ineligible ones must reap exactly the one.
        var reapable = BuildSession(
            JellyRockSessionService.JellyRockClientName, checkInSecondsAgo: 90, isPaused: false, positionSeconds: 690, id: "stale");
        var fresh = BuildSession(
            JellyRockSessionService.JellyRockClientName, checkInSecondsAgo: 20, isPaused: false, positionSeconds: 300, id: "fresh");
        var paused = BuildSession(
            JellyRockSessionService.JellyRockClientName, checkInSecondsAgo: 300, isPaused: true, positionSeconds: 300, id: "paused");
        var nonJellyRock = BuildSession(
            "Jellyfin Web", checkInSecondsAgo: 300, isPaused: false, positionSeconds: 300, id: "web");
        _sessionManager.Sessions.Returns(new[] { fresh, reapable, paused, nonJellyRock });

        await BuildService().SweepAsync();

        await _sessionManager.Received(1).OnPlaybackStopped(Arg.Any<PlaybackStopInfo>());
        await _sessionManager.Received(1).OnPlaybackStopped(Arg.Is<PlaybackStopInfo>(i => i.SessionId == "stale"));
    }

    [Fact]
    public async Task SweepAsync_OneSessionThrows_StillReapsTheRest()
    {
        // The per-session try/catch must isolate a failing reap so the sweep finishes the remaining
        // sessions — verifies the resilience the SweepAsync catch block promises.
        var bad = BuildSession(
            JellyRockSessionService.JellyRockClientName, checkInSecondsAgo: 90, isPaused: false, positionSeconds: 690, id: "bad");
        var good = BuildSession(
            JellyRockSessionService.JellyRockClientName, checkInSecondsAgo: 90, isPaused: false, positionSeconds: 690, id: "good");
        _sessionManager.Sessions.Returns(new[] { bad, good });
        _sessionManager
            .When(x => x.OnPlaybackStopped(Arg.Is<PlaybackStopInfo>(i => i.SessionId == "bad")))
            .Do(_ => throw new InvalidOperationException("reap boom"));

        await BuildService().SweepAsync();

        // The good session is still reaped despite the bad one throwing.
        await _sessionManager.Received(1).OnPlaybackStopped(Arg.Is<PlaybackStopInfo>(i => i.SessionId == "good"));
    }

    [Fact]
    public async Task SweepAsync_NonJellyRockClient_DoesNotReap()
    {
        var session = BuildSession(
            client: "Jellyfin Web",
            checkInSecondsAgo: 300,
            isPaused: false,
            positionSeconds: 300);
        _sessionManager.Sessions.Returns(new[] { session });

        await BuildService().SweepAsync();

        await _sessionManager.DidNotReceive().OnPlaybackStopped(Arg.Any<PlaybackStopInfo>());
    }

    private PlaybackReaperService BuildService() =>
        new(_sessionManager, Substitute.For<ILogger<PlaybackReaperService>>());

    private SessionInfo BuildSession(
        string client,
        int checkInSecondsAgo,
        bool isPaused,
        int positionSeconds,
        string appVersion = "2.23.0",
        string id = "sess-1") =>
        new(_sessionManager, Substitute.For<ILogger>())
        {
            Id = id,
            Client = client,
            ApplicationVersion = appVersion,
            DeviceName = "Living Room Roku",
            LastPlaybackCheckIn = DateTime.UtcNow.AddSeconds(-checkInSecondsAgo),
            NowPlayingItem = new BaseItemDto { Id = Guid.NewGuid(), RunTimeTicks = TimeSpan.FromMinutes(90).Ticks },
            PlayState = new PlayerStateInfo
            {
                IsPaused = isPaused,
                PositionTicks = TimeSpan.FromSeconds(positionSeconds).Ticks,
                MediaSourceId = "ms-1"
            }
        };
}
