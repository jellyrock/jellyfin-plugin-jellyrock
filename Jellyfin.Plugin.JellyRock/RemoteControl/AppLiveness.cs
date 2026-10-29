namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// What the plugin can tell about whether a JellyRock app is running, read once per session by
/// <see cref="ReapDecision.ReadLiveness"/>. ADR 0001 defines when a long-poll app counts as closed.
/// </summary>
public enum AppLiveness
{
    /// <summary>
    /// The app has never polled and no controller says it is open: an app on an HTTP server whose
    /// socket is gone, or one too old for the long poll. Never read as closed.
    /// </summary>
    Unknown,

    /// <summary>
    /// A controller other than the cold-launch phantom says the app is open: a fresh poll, or the
    /// server's session socket.
    /// </summary>
    Open,

    /// <summary>
    /// The app polled at least once and no controller says it is open any more.
    /// </summary>
    Closed
}
