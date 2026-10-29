using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.Requests;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Always-on background worker that frees a JellyRock playback session shortly after the Roku app is
/// closed mid-playback on an <b>HTTPS</b> server (JellyRock issue #43). Roku cannot run code when the
/// user presses Home, so the app can never tell the server it stopped. On an HTTP server this already
/// resolves itself: JellyRock holds Jellyfin's native session WebSocket, so when the app closes the
/// socket drops and the server removes the session within seconds. On HTTPS, Roku has no socket TLS
/// (no <c>wss://</c>), so nothing signals the close: the session lingers until Jellyfin's own idle
/// reaper clears it, but only after a coarse ~5-10 minute check, leaving any active transcode running
/// and the dashboard showing a phantom "now playing" until then.
/// <para>
/// This worker sweeps the session list on a short cadence and reaps any JellyRock session whose app
/// has closed its long poll (<see cref="ReapDecision.IsClosedAppReapable"/>), playing or paused, and any
/// that is actively playing but whose last real playback check-in (the client's ~10s <c>Sessions/Playing/Progress</c>
/// report) has gone stale past <see cref="StaleThresholdSeconds"/>. The check-in signal is plain REST,
/// so the mechanism itself is transport-agnostic; in practice it only fires on HTTPS, since on HTTP the
/// native session socket has already removed the closed-app session before this window elapses (so it
/// also stays a harmless safety net there). Reaping calls <see cref="ISessionManager.OnPlaybackStopped"/>,
/// exactly as Jellyfin's own idle check does, so it frees any active transcode and clears the session
/// through the normal path, just on a faster, purpose-built clock. See <see cref="ReapDecision"/> for the
/// eligibility rule and the resume-position correction (which keeps the recorded resume point at the
/// user's real stop rather than the server's forward-extrapolated one).
/// </para>
/// <para>
/// The same sweep takes a closed JellyRock app out of its SyncPlay group
/// (<see cref="ReapDecision.ShouldLeaveSyncPlayGroup"/>), through the <c>LeaveGroup</c> call the server
/// makes itself when a session ends. It leaves only the group and never ends the session, because the
/// cold-launch wake publishes its phantom cast target on that session.
/// </para>
/// </summary>
public sealed class PlaybackReaperService : IHostedService, IDisposable
{
    /// <summary>
    /// Stale threshold, in seconds. Six consecutive missed ~10s check-ins: a decisive "the app is gone
    /// or hard-stalled" signal, ~5-8x faster than Jellyfin's built-in idle reap. A fixed constant, not
    /// a setting — the plugin has no user-tunable configuration, and Jellyfin's own analogous idle
    /// threshold is likewise hardcoded. The resume-position correction (see <see cref="ReapDecision"/>)
    /// decouples resume accuracy from this value, so it is chosen purely for false-positive safety.
    /// <para>
    /// A mid-playback buffer is NOT a false positive: verified against the JellyRock client, the ~10s
    /// report timer keeps firing while buffering (only pause/stop/finished/error stop it), so a
    /// transiently-stalled-but-alive session keeps checking in and is never reaped; and a non-progressing
    /// stall is escalated by the client's own buffer-check to a self-reported stop within ~30s. This
    /// threshold therefore only elapses for a client that is genuinely unreachable — the correct case to
    /// reap — which is also why 60s is safe rather than aggressive.
    /// </para>
    /// </summary>
    private const int StaleThresholdSeconds = 60;

    /// <summary>
    /// Sweep cadence, in seconds. Every action lands at most one sweep after its condition holds: a
    /// playback reap after <c>StaleThresholdSeconds</c>, and a SyncPlay leave a few seconds after the app
    /// closes its poll, which is why the sweep is short. A whole-list scan is negligible, so, unlike
    /// Jellyfin's SessionManager, this worker does not start and stop the timer with playback.
    /// </summary>
    private const int SweepIntervalSeconds = 5;

    private readonly ISessionManager _sessionManager;
    private readonly ISyncPlayManager _syncPlayManager;
    private readonly ILogger<PlaybackReaperService> _logger;

    private PeriodicTimer? _timer;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackReaperService"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="syncPlayManager">The SyncPlay manager.</param>
    /// <param name="logger">Logger.</param>
    public PlaybackReaperService(ISessionManager sessionManager, ISyncPlayManager syncPlayManager, ILogger<PlaybackReaperService> logger)
    {
        _sessionManager = sessionManager;
        _syncPlayManager = syncPlayManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[JellyRock] playback reaper starting; stale threshold {Threshold}s, sweep every {Interval}s",
            StaleThresholdSeconds,
            SweepIntervalSeconds);

        _timer = new PeriodicTimer(TimeSpan.FromSeconds(SweepIntervalSeconds));
        _loop = RunLoopAsync(_timer);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Disposing the timer makes WaitForNextTickAsync return false, so the loop exits on its own.
        _timer?.Dispose();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
            _loop = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
    }

    private async Task RunLoopAsync(PeriodicTimer timer)
    {
        // WaitForNextTickAsync only fires after the previous SweepAsync has completed, so sweeps never
        // overlap — no re-entrancy guard needed. It returns false once the timer is disposed (shutdown).
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                await SweepAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyRock] playback reaper sweep failed");
            }
        }
    }

    /// <summary>
    /// Scans the current sessions once: takes every closed JellyRock app out of its SyncPlay group, and
    /// reaps every JellyRock session eligible per <see cref="ReapDecision.IsReapable"/>. Internal so the
    /// test project can drive one sweep directly without the timer.
    /// </summary>
    /// <returns>A task that completes when the sweep finishes.</returns>
    internal async Task SweepAsync()
    {
        var now = DateTime.UtcNow;

        foreach (var session in _sessionManager.Sessions)
        {
            LeaveSyncPlayGroupIfClosed(session);

            var isPaused = session.PlayState?.IsPaused ?? false;
            var hasNowPlaying = session.NowPlayingItem is not null;
            var queueing = session.SessionControllers.OfType<QueueingSessionController>().FirstOrDefault();
            var closedApp = queueing is not null
                && ReapDecision.IsClosedAppReapable(
                    session.Client,
                    hasNowPlaying,
                    queueing.HasPolled,
                    PhantomSessionService.IsAppOpen(session));
            if (!closedApp
                && !ReapDecision.IsReapable(
                    session.Client,
                    session.ApplicationVersion,
                    hasNowPlaying,
                    isPaused,
                    session.LastPlaybackCheckIn,
                    now,
                    StaleThresholdSeconds))
            {
                continue;
            }

            var nowPlaying = session.NowPlayingItem;
            var correctedTicks = ReapDecision.ReapPositionTicks(
                isPaused,
                session.PlayState?.PositionTicks ?? 0,
                session.LastPlaybackCheckIn,
                now,
                nowPlaying?.RunTimeTicks);

            try
            {
                // Mirror Jellyfin's own CheckForIdlePlayback stop, with the de-extrapolated position.
                // OnPlaybackStopped clears session.NowPlayingItem synchronously (via RemoveNowPlayingItem)
                // before it awaits, so a reaped session fails the hasNowPlaying gate on the next sweep —
                // no re-reap, no de-dupe bookkeeping needed. (Verified in server 10.9-10.11.)
                await _sessionManager.OnPlaybackStopped(new PlaybackStopInfo
                {
                    Item = nowPlaying,
                    ItemId = nowPlaying?.Id ?? Guid.Empty,
                    SessionId = session.Id,
                    MediaSourceId = session.PlayState?.MediaSourceId,
                    PositionTicks = correctedTicks
                }).ConfigureAwait(false);

                _logger.LogInformation(
                    "[JellyRock] reaped idle playback session {SessionId} ({DeviceName}); resume set to {Ticks} ticks; app closed its poll: {AppClosed}",
                    session.Id,
                    session.DeviceName,
                    correctedTicks,
                    closedApp);
            }
            catch (Exception ex)
            {
                // One bad session must not abort the sweep (mirrors the server's per-session try/catch).
                // Logged at Warning, not Debug: a reap that keeps failing is an operator-visible problem
                // (a session that won't clear), not routine noise — one line per failed sweep at most.
                _logger.LogWarning(ex, "[JellyRock] error reaping idle playback session {SessionId}", session.Id);
            }
        }
    }

    private void LeaveSyncPlayGroupIfClosed(SessionInfo session)
    {
        var controller = session.SessionControllers.OfType<QueueingSessionController>().FirstOrDefault();
        if (controller is null
            || !ReapDecision.ShouldLeaveSyncPlayGroup(
                session.Client,
                controller.IsInSyncPlayGroup,
                controller.HasPolled,
                PhantomSessionService.IsAppOpen(session)))
        {
            return;
        }

        try
        {
            _syncPlayManager.LeaveGroup(session, new LeaveGroupRequest(), CancellationToken.None);
            _logger.LogInformation(
                "[JellyRock] left the SyncPlay group of closed session {SessionId} ({DeviceName}); app closed its poll: {PollClosed}",
                session.Id,
                session.DeviceName,
                controller.PollClosedByClient);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JellyRock] error leaving the SyncPlay group of session {SessionId}", session.Id);
        }
        finally
        {
            // The server's GroupLeft frame clears this too. Clearing it here as well means a leave the
            // server refused is tried once, not on every sweep.
            controller.ForgetSyncPlayGroup();
        }
    }
}
