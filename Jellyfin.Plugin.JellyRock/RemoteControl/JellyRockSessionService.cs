using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Always-on background worker that keeps JellyRock sessions controllable on HTTPS. For each session
/// whose <c>Client</c> is JellyRock it forces the media-control capability (JellyRock advertises it
/// false on https, since it can't ws://) and attaches a <see cref="QueueingSessionController"/> so the
/// server has somewhere to fan cast commands. Whether the session is actually advertised as a cast
/// target is then gated live by the controller's poll liveness — so this only ever ADDS
/// controllability; it never has to retract it on a timer.
/// </summary>
public sealed class JellyRockSessionService : IHostedService
{
    /// <summary>
    /// The session <c>Client</c> string that identifies a JellyRock session. JellyRock sends
    /// <c>Client="JellyRock"</c> in its auth header; only matching sessions are made controllable and
    /// only they may consume the long-poll channel. A constant, not a setting — the client always
    /// identifies this way, so there is nothing for an admin to tune.
    /// </summary>
    public const string JellyRockClientName = "JellyRock";

    // Attachment does a non-atomic read-modify-write of SessionInfo.SessionControllers (via
    // EnsureController) AND is triggered from two places (this service on session/capability events,
    // and the poll endpoint as a fallback). Serialize all attaches so two racing callers can't each
    // create a controller and clobber one — which would leave the server fanning commands to a
    // controller the poll never drains.
    private static readonly object AttachLock = new();

    private readonly ISessionManager _sessionManager;
    private readonly ILogger<JellyRockSessionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyRockSessionService"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="logger">Logger.</param>
    public JellyRockSessionService(ISessionManager sessionManager, ILogger<JellyRockSessionService> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Idempotently forces the media-control capability and attaches a
    /// <see cref="QueueingSessionController"/> to a session, returning the controller. Shared by this
    /// service (on session/capability events) and the long-poll endpoint (fallback, in case a poll
    /// races ahead of the session event). Thread-safe via <see cref="AttachLock"/>.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="session">The session to make controllable.</param>
    /// <returns>The attached <see cref="QueueingSessionController"/>.</returns>
    public static QueueingSessionController EnsureAttached(ISessionManager sessionManager, SessionInfo session)
    {
        lock (AttachLock)
        {
            // Force SupportsMediaControl=true directly (NOT ReportCapabilities, which would re-raise
            // CapabilitiesChanged and loop). SessionInfo.SupportsRemoteControl reads this flag live.
            // Capture the reference once: a concurrent ReportCapabilities can swap session.Capabilities
            // out between a null-check and the assignment (it is a settable property), so re-reading it
            // would risk an NRE. If it *is* swapped after capture, the CapabilitiesChanged event re-runs
            // this attach against the new object, so nothing is lost.
            var capabilities = session.Capabilities;
            if (capabilities is not null && !capabilities.SupportsMediaControl)
            {
                capabilities.SupportsMediaControl = true;
            }

            var ensured = session.EnsureController<QueueingSessionController>(_ => new QueueingSessionController());
            if (ensured.Item2)
            {
                sessionManager.OnSessionControllerConnected(session);
            }

            return (QueueingSessionController)ensured.Item1;
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[JellyRock] session service starting; matching Client='{Client}'", JellyRockClientName);

        _sessionManager.SessionStarted += OnSessionChanged;
        _sessionManager.CapabilitiesChanged += OnSessionChanged;

        // A JellyRock session may already exist (plugin enabled after the app connected).
        foreach (var session in _sessionManager.Sessions)
        {
            TryAttach(session);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.SessionStarted -= OnSessionChanged;
        _sessionManager.CapabilitiesChanged -= OnSessionChanged;
        return Task.CompletedTask;
    }

    private void OnSessionChanged(object? sender, SessionEventArgs e)
    {
        var session = e?.SessionInfo;
        if (session is not null)
        {
            TryAttach(session);
        }
    }

    private void TryAttach(SessionInfo session)
    {
        if (!string.Equals(session.Client, JellyRockClientName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureAttached(_sessionManager, session);
    }
}
