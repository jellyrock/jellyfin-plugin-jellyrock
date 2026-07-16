using System;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Pure decision logic for <see cref="PlaybackReaperService"/>, deliberately free of Jellyfin session
/// types so the two judgment calls — whether a session should be reaped, and what resume position to
/// record when it is — are trivially unit-testable with primitive inputs. The service reads the live
/// <c>SessionInfo</c> fields and forwards them here; all the reasoning lives in these two methods.
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
