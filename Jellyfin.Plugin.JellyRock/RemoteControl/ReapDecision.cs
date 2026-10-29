using System;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Pure decision logic for <see cref="PlaybackReaperService"/>, free of Jellyfin session types so its
/// judgment calls (whether to reap a session, what resume position to record, and whether a closed app
/// leaves its SyncPlay group) are unit-testable with primitive inputs. The service reads the live
/// <c>SessionInfo</c> fields and controller state and forwards them here; all the reasoning lives here.
/// </summary>
public static class ReapDecision
{
    /// <summary>
    /// Minimum JellyRock version whose playing-video progress reports are frequent enough for the 60s
    /// stale threshold to be safe. JellyRock moved its playing-video report cadence from every 30s to
    /// every ~10s in <c>v1.15.0</c>. Audio and paused video report less often, so the cadence holds only
    /// for a playing video. At 10s the threshold is a comfortable ~6x the report interval, so a healthy
    /// client is never reaped between reports. Older clients (~30s reports) get only ~2x margin, where a
    /// merely-late report or a short buffered network blip could reap a still-playing session — so they
    /// are excluded and left to Jellyfin's own idle reap, which is exactly their behavior today (no
    /// regression). A client whose version cannot be parsed is treated as ineligible (fail safe toward
    /// not reaping).
    /// </summary>
    private static readonly Version MinReportCadenceVersion = new(1, 15, 0);

    /// <summary>
    /// What the plugin can tell about whether the app is running (ADR 0001). A controller other than the
    /// cold-launch phantom that says the app is open (<see cref="PhantomSessionService.IsAppOpen"/>) makes it
    /// <see cref="AppLiveness.Open"/>. Otherwise an app that has polled at least once is
    /// <see cref="AppLiveness.Closed"/>, and one that never polled is <see cref="AppLiveness.Unknown"/>: on
    /// HTTP the server ends the session itself when the socket drops, and an app too old for the long poll
    /// gives no signal.
    /// </summary>
    /// <param name="hasPolled">Whether the app has polled the plugin at least once.</param>
    /// <param name="appOpen">Whether any controller of the session says the app is open.</param>
    /// <returns>The app's liveness.</returns>
    public static AppLiveness ReadLiveness(bool hasPolled, bool appOpen)
    {
        if (appOpen)
        {
            return AppLiveness.Open;
        }

        return hasPolled ? AppLiveness.Closed : AppLiveness.Unknown;
    }

    /// <summary>
    /// Whether to reap a JellyRock session with something playing or paused, and why.
    /// <list type="bullet">
    ///   <item><see cref="AppLiveness.Open"/>: never. A paused app checks in rarely or not at all, and the
    ///     server can lose a pause report and keep <c>IsPaused=false</c>, so an open app can look
    ///     stale.</item>
    ///   <item><see cref="AppLiveness.Closed"/>: <see cref="ReapReason.AppClosed"/>, playing or paused,
    ///     without waiting for the check-ins to go stale.</item>
    ///   <item><see cref="AppLiveness.Unknown"/>: <see cref="ReapReason.CheckInsStale"/> when the session is
    ///     playing, new enough to report every ~10s while playing video (see
    ///     <see cref="MinReportCadenceVersion"/>), and its
    ///     last check-in is older than <paramref name="thresholdSeconds"/>.</item>
    /// </list>
    /// <para>
    /// With no poll to say the app closed, a paused session is left to Jellyfin's own idle check, since a
    /// paused app checks in rarely or not at all. This trusts the server's <c>IsPaused</c>, which a lost
    /// pause report leaves false: Jellyfin's 1-second automatic progress copy of the last playing report
    /// can land after the pause report is applied and before it replaces the stored report.
    /// </para>
    /// </summary>
    /// <param name="client">The session's <c>Client</c> string.</param>
    /// <param name="appVersion">The session's <c>ApplicationVersion</c> string.</param>
    /// <param name="hasNowPlaying">Whether the session currently has a NowPlayingItem.</param>
    /// <param name="isPaused">Whether the session's playstate is paused.</param>
    /// <param name="liveness">The app's liveness (<see cref="ReadLiveness"/>).</param>
    /// <param name="lastPlaybackCheckIn">The session's last real playback check-in (UTC).</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <param name="thresholdSeconds">Stale threshold in seconds.</param>
    /// <returns>Why to reap the session, or <see cref="ReapReason.None"/>.</returns>
    public static ReapReason Decide(
        string? client,
        string? appVersion,
        bool hasNowPlaying,
        bool isPaused,
        AppLiveness liveness,
        DateTime lastPlaybackCheckIn,
        DateTime nowUtc,
        int thresholdSeconds)
    {
        if (!IsJellyRock(client) || !hasNowPlaying)
        {
            return ReapReason.None;
        }

        return liveness switch
        {
            AppLiveness.Open => ReapReason.None,
            AppLiveness.Closed => ReapReason.AppClosed,
            _ => HasFastReportCadence(appVersion)
                && !isPaused
                && (nowUtc - lastPlaybackCheckIn).TotalSeconds > thresholdSeconds
                    ? ReapReason.CheckInsStale
                    : ReapReason.None
        };
    }

    /// <summary>
    /// Whether to take a JellyRock session out of its SyncPlay group: the session is in a group and the
    /// app has closed (<see cref="AppLiveness.Closed"/>). A Roku app can't send <c>Leave</c> as it exits,
    /// and on HTTPS no socket drops to end the session, so without this the group keeps a member that
    /// never reports ready and waits on it at every seek or item change.
    /// </summary>
    /// <param name="client">The session's <c>Client</c> string.</param>
    /// <param name="inSyncPlayGroup">Whether the server's frames to the session put it in a group.</param>
    /// <param name="liveness">The app's liveness (<see cref="ReadLiveness"/>).</param>
    /// <returns><c>true</c> if the session should leave its group.</returns>
    public static bool ShouldLeaveSyncPlayGroup(string? client, bool inSyncPlayGroup, AppLiveness liveness)
    {
        return IsJellyRock(client) && inSyncPlayGroup && liveness == AppLiveness.Closed;
    }

    private static bool IsJellyRock(string? client)
    {
        return string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the reported JellyRock version reports playing-video progress every ~10s (v1.15.0+), so
    /// the stale threshold has enough margin for a playing video. Unparseable / missing versions return <c>false</c>.
    /// </summary>
    /// <param name="appVersion">The session's <c>ApplicationVersion</c> string.</param>
    /// <returns><c>true</c> if the version is new enough for the fast report cadence.</returns>
    private static bool HasFastReportCadence(string? appVersion)
    {
        return Version.TryParse(appVersion, out var version) && version >= MinReportCadenceVersion;
    }

    /// <summary>
    /// The resume position to record when reaping, in ticks: the paused position as it stands, since the
    /// server's auto-progress timer does not advance a paused session, or else
    /// <see cref="CorrectedPositionTicks"/>.
    /// </summary>
    /// <param name="isPaused">Whether the session's playstate is paused.</param>
    /// <param name="positionTicks">The session's current PositionTicks.</param>
    /// <param name="lastPlaybackCheckIn">The session's last real playback check-in (UTC).</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <param name="runtimeTicks">The item runtime in ticks, if known (upper clamp).</param>
    /// <returns>The resume position in ticks.</returns>
    public static long ReapPositionTicks(
        bool isPaused,
        long positionTicks,
        DateTime lastPlaybackCheckIn,
        DateTime nowUtc,
        long? runtimeTicks)
    {
        // Paused: no drift to back out (now minus now), so only the clamp applies.
        return isPaused
            ? CorrectedPositionTicks(positionTicks, nowUtc, nowUtc, runtimeTicks)
            : CorrectedPositionTicks(positionTicks, lastPlaybackCheckIn, nowUtc, runtimeTicks);
    }

    /// <summary>
    /// The resume position to record when reaping, in ticks.
    /// <para>
    /// Jellyfin's <c>SessionInfo</c> runs an auto-progress timer that advances <c>PositionTicks</c> by
    /// real elapsed time between the client's ~10s check-ins — it assumes the client is still playing
    /// and extrapolates the clock forward. Once we have decided the client is gone, that extrapolation
    /// rests on a false premise: left as-is it would record a resume point minutes past where the user
    /// actually stopped (they would skip content). So we back it out — subtract the time elapsed since
    /// the last real check-in — landing on the last client-confirmed position. That is at most one
    /// check-in interval (~10s) behind the true stop, i.e. in the safe re-watch direction, and it does
    /// not drift with how long we waited to reap. Clamped to <c>[0, runtimeTicks]</c>.
    /// </para>
    /// </summary>
    /// <param name="positionTicks">The session's current (auto-advanced) PositionTicks.</param>
    /// <param name="lastPlaybackCheckIn">The session's last real playback check-in (UTC).</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <param name="runtimeTicks">The item runtime in ticks, if known (upper clamp).</param>
    /// <returns>The de-extrapolated resume position in ticks.</returns>
    public static long CorrectedPositionTicks(
        long positionTicks,
        DateTime lastPlaybackCheckIn,
        DateTime nowUtc,
        long? runtimeTicks)
    {
        // TimeSpan.Ticks and PositionTicks share the same 100 ns unit, so this is a direct subtraction.
        var driftTicks = (nowUtc - lastPlaybackCheckIn).Ticks;
        var corrected = positionTicks - driftTicks;

        if (corrected < 0)
        {
            corrected = 0;
        }

        if (runtimeTicks is { } runtime && corrected > runtime)
        {
            corrected = runtime;
        }

        return corrected;
    }
}
