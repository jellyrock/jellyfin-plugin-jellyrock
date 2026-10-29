namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Why <see cref="ReapDecision.Decide"/> reaps a playback session, or <see cref="None"/> to leave it.
/// </summary>
public enum ReapReason
{
    /// <summary>
    /// Leave the session alone.
    /// </summary>
    None,

    /// <summary>
    /// The app has closed (<see cref="AppLiveness.Closed"/>), playing or paused.
    /// </summary>
    AppClosed,

    /// <summary>
    /// The app never polled, and its playing session's check-ins went stale.
    /// </summary>
    CheckInsStale
}
