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
    /// Minimum JellyRock version whose playback progress reports are frequent enough for the 60s stale
    /// threshold to be safe. JellyRock moved its report cadence from every 30s to every ~10s in
    /// <c>v1.15.0</c>; at 10s the threshold is a comfortable ~6x the report interval, so a healthy
    /// client is never reaped between reports. Older clients (~30s reports) get only ~2x margin, where a
    /// merely-late report or a short buffered network blip could reap a still-playing session — so they
    /// are excluded and left to Jellyfin's own idle reap, which is exactly their behavior today (no
    /// regression). A client whose version cannot be parsed is treated as ineligible (fail safe toward
    /// not reaping).
    /// </summary>
    private static readonly Version MinReportCadenceVersion = new(1, 15, 0);

    /// <summary>
    /// Whether a session should be fast-reaped: it is a JellyRock session, new enough to report
    /// playback progress every ~10s (see <see cref="MinReportCadenceVersion"/>), that is actively
    /// playing (not paused) whose last real playback check-in is older than
    /// <paramref name="thresholdSeconds"/>.
    /// <para>
    /// Paused sessions are deliberately excluded. A paused JellyRock app stops sending progress
    /// check-ins (its report timer stops on pause), so a genuinely-paused-but-alive session would look
    /// stale here and be wrongly reaped. Abandoned-while-paused is therefore left to Jellyfin's own
    /// idle/inactive reaping rather than fast-killed by this plugin — a paused transcode is low-cost
    /// and the resume-position correction below does not apply while paused.
    /// </para>
    /// <para>
    /// This exclusion is load-bearing and depends on client behavior that has been verified in the
    /// JellyRock source: on pause the client sends exactly one progress report with <c>IsPaused=true</c>
    /// and then stops its report timer (VideoPlayerView.bs <c>onState</c> "paused" branch). That single
    /// report is why <paramref name="isPaused"/> can be trusted here even though check-ins then go stale —
    /// the server's paused flag is set before the silence begins. If a future client stopped reporting on
    /// pause WITHOUT that final paused report, this gate would no longer protect paused-but-alive
    /// sessions, so keep the two in sync.
    /// </para>
    /// <para>
    /// A paused app that has closed is still reaped, through <see cref="IsClosedAppReapable"/>: its
    /// closed long poll is a signal that does not depend on check-ins.
    /// </para>
    /// </summary>
    /// <param name="client">The session's <c>Client</c> string.</param>
    /// <param name="appVersion">The session's <c>ApplicationVersion</c> string.</param>
    /// <param name="hasNowPlaying">Whether the session currently has a NowPlayingItem.</param>
    /// <param name="isPaused">Whether the session's playstate is paused.</param>
    /// <param name="lastPlaybackCheckIn">The session's last real playback check-in (UTC).</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <param name="thresholdSeconds">Stale threshold in seconds.</param>
    /// <returns><c>true</c> if the session should be reaped.</returns>
    public static bool IsReapable(
        string? client,
        string? appVersion,
        bool hasNowPlaying,
        bool isPaused,
        DateTime lastPlaybackCheckIn,
        DateTime nowUtc,
        int thresholdSeconds)
    {
        return string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase)
            && HasFastReportCadence(appVersion)
            && hasNowPlaying
            && !isPaused
            && (nowUtc - lastPlaybackCheckIn).TotalSeconds > thresholdSeconds;
    }

    /// <summary>
    /// Whether a JellyRock app that polls the plugin has closed: it has polled at least once, and no
    /// controller of its session says the app is open (<see cref="PhantomSessionService.IsAppOpen"/>).
    /// The first condition keeps an app that never polls (on an HTTP server, or too old for the long
    /// poll) from reading as closed; on HTTP the server ends the session itself when the socket drops.
    /// </summary>
    /// <param name="hasPolled">Whether the app has polled the plugin at least once.</param>
    /// <param name="appOpen">Whether any controller of the session says the app is open.</param>
    /// <returns><c>true</c> if the app has closed.</returns>
    public static bool IsLongPollAppClosed(bool hasPolled, bool appOpen) => hasPolled && !appOpen;

    /// <summary>
    /// Whether to reap a JellyRock session whose app has closed (<see cref="IsLongPollAppClosed"/>) with
    /// something still playing or paused. Unlike <see cref="IsReapable"/> this needs no check-ins, so it
    /// covers an app closed while paused, and it acts as soon as the app's poll is gone.
    /// </summary>
    /// <param name="client">The session's <c>Client</c> string.</param>
    /// <param name="hasNowPlaying">Whether the session currently has a NowPlayingItem.</param>
    /// <param name="hasPolled">Whether the app has polled the plugin at least once.</param>
    /// <param name="appOpen">Whether any controller of the session says the app is open.</param>
    /// <returns><c>true</c> if the session should be reaped.</returns>
    public static bool IsClosedAppReapable(string? client, bool hasNowPlaying, bool hasPolled, bool appOpen)
    {
        return string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase)
            && hasNowPlaying
            && IsLongPollAppClosed(hasPolled, appOpen);
    }

    /// <summary>
    /// Whether to take a JellyRock session out of its SyncPlay group: the session is in a group and the
    /// app has closed (<see cref="IsLongPollAppClosed"/>). A Roku app can't send <c>Leave</c> as it exits,
    /// and on HTTPS no socket drops to end the session, so without this the group keeps a member that
    /// never reports ready and waits on it at every seek or item change.
    /// </summary>
    /// <param name="client">The session's <c>Client</c> string.</param>
    /// <param name="inSyncPlayGroup">Whether the server's frames to the session put it in a group.</param>
    /// <param name="hasPolled">Whether the app has polled the plugin at least once.</param>
    /// <param name="appOpen">Whether any controller of the session says the app is open.</param>
    /// <returns><c>true</c> if the session should leave its group.</returns>
    public static bool ShouldLeaveSyncPlayGroup(string? client, bool inSyncPlayGroup, bool hasPolled, bool appOpen)
    {
        return string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase)
            && inSyncPlayGroup
            && IsLongPollAppClosed(hasPolled, appOpen);
    }

    /// <summary>
    /// Whether the reported JellyRock version reports playback progress every ~10s (v1.15.0+), so the
    /// stale threshold has enough margin. Unparseable / missing versions return <c>false</c>.
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
