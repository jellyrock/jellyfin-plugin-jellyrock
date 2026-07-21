using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// Always-on worker that publishes the cold-launch <b>phantom</b> cast target (issue #668, P2): for each
/// validated pairing whose Roku the server can currently reach, while JellyRock is <b>closed</b>, it mints
/// a <c>SessionInfo</c> via <see cref="ISessionManager.LogSessionActivity"/> with no live client ever
/// connecting, advertises the reduced closed-state capability set, and attaches a
/// <see cref="PhantomSessionController"/> that cold-wakes the Roku over ECP when selected. Selecting the
/// phantom in jellyfin-web's "Play On" while the app is closed is the closed→open bridge; once the app wakes,
/// control hands off to the live #666/#667 long-poll channel.
///
/// <para><b>One presence per device.</b> <see cref="ISessionManager.LogSessionActivity"/> keys a session by
/// device + client + user, so the phantom and the app's real session are the <b>same</b> <c>SessionInfo</c> —
/// open and closed are one continuous identity keyed by the paired Jellyfin <c>DeviceId</c>. That makes the
/// open/closed swap a matter of who owns the session's capabilities at any moment:</para>
/// <list type="bullet">
///   <item><b>Closed</b> (no live non-phantom controller — see <see cref="IsAppOpen"/>): this service forces the
///     reduced <c>SupportedCommands=[DisplayContent]</c> set and keeps the <see cref="PhantomSessionController"/>
///     live, so the web renders only the wake affordance and no no-op transport controls.</item>
///   <item><b>Open</b> (<see cref="IsAppOpen"/> — a live long-poll controller on HTTPS <em>or</em> native
///     <c>ws://</c> controller on HTTP): the live session reports its own full capabilities; this
///     service must <b>not</b> touch it — it neither refreshes activity nor rewrites capabilities, so it can
///     never stomp the open app's controls. The phantom controller stays attached but self-suppresses its ECP
///     wake while open (see <see cref="PhantomSessionController"/>).</item>
/// </list>
///
/// <para><b>Reaper-safe.</b> A phantom has no <c>NowPlayingItem</c>, and <see cref="ReapDecision.IsReapable"/>
/// gates on <c>hasNowPlaying</c>, so <see cref="PlaybackReaperService"/> can never reap it. A <b>revoked</b>
/// phantom (Roku powered off / paired-out) simply stops being refreshed and idle-expires through Jellyfin's
/// own path.</para>
/// </summary>
public sealed class PhantomSessionService : IHostedService, IDisposable
{
    /// <summary>
    /// Refresh cadence, in seconds. Each tick re-probes reachability and republishes/revokes every pairing.
    /// Comfortably under Jellyfin's coarse idle-session expiry so a live phantom is never idle-reaped between
    /// ticks, and short enough that a Roku going dark drops from the cast list within a couple of ticks. A
    /// fixed constant, not a setting — the plugin has no user-tunable configuration.
    /// </summary>
    private const int RefreshIntervalSeconds = 30;

    /// <summary>
    /// Cosmetic <c>appVersion</c> for a phantom minted with no prior live session (<see cref="ISessionManager.LogSessionActivity"/>
    /// requires a non-empty value). When a real session for the device already exists its reported version is
    /// reused instead; this floor is only ever shown before the app has connected once.
    /// </summary>
    private const string PhantomAppVersion = "2.23.0";

    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PairingValidationService _pairingValidation;
    private readonly ILogger<PhantomSessionService> _logger;

    private PeriodicTimer? _timer;
    private Task? _loop;

    /// <summary>Initializes a new instance of the <see cref="PhantomSessionService"/> class.</summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="userManager">Resolves the pairing's owning user (the <c>User</c> entity LogSessionActivity needs).</param>
    /// <param name="httpClientFactory">Factory for the phantom controller's ECP client.</param>
    /// <param name="pairingValidation">The ECP reachability probe, reused for the advertise-time re-check.</param>
    /// <param name="logger">Logger.</param>
    public PhantomSessionService(
        ISessionManager sessionManager,
        IUserManager userManager,
        IHttpClientFactory httpClientFactory,
        PairingValidationService pairingValidation,
        ILogger<PhantomSessionService> logger)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _pairingValidation = pairingValidation;
        _logger = logger;
    }

    /// <summary>
    /// Whether JellyRock is currently <b>open</b> for this session — <b>transport-agnostic</b>: any live
    /// session controller OTHER than our own phantom is attached. This must span both remote-control
    /// transports, because the app's open-state signal differs by server scheme (issue #668 HTTP fix):
    /// <list type="bullet">
    ///   <item>On <b>HTTPS</b> the open app drives our long-poll — a poll-fresh
    ///     <see cref="QueueingSessionController"/> (alive iff a poll is fresh).</item>
    ///   <item>On <b>HTTP</b> the open app uses Jellyfin's native session socket — a
    ///     <c>WebSocketController</c> (alive iff a socket is open). The app never polls our long-poll on
    ///     HTTP, so a <see cref="QueueingSessionController"/>-only check reads a wide-open HTTP app as
    ///     closed, and the service keeps forcing reduced caps + re-firing the ECP wake onto the live
    ///     native session.</item>
    /// </list>
    /// Both native controllers report <see cref="ISessionController.IsSessionActive"/> from their live
    /// transport, so testing every non-phantom controller is the one open signal that spans both — the same
    /// shape Jellyfin's own <c>SessionManager</c> uses for session liveness. The <see cref="PhantomSessionController"/>
    /// is excluded: it reports active while advertising a <em>closed</em> app, so counting it would read every
    /// published phantom as "open". Shared with <see cref="PhantomSessionController"/> so both the publish guard
    /// and the wake suppression use one definition of "open".
    /// </summary>
    /// <param name="session">The session to test.</param>
    /// <returns><c>true</c> if the app is open (a live non-phantom controller is attached).</returns>
    internal static bool IsAppOpen(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.SessionControllers.Any(c => c is not PhantomSessionController && c.IsSessionActive);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[JellyRock] cold-launch phantom service starting; refresh every {Interval}s", RefreshIntervalSeconds);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(RefreshIntervalSeconds));
        _loop = RunLoopAsync(_timer);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
            _loop = null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _timer?.Dispose();

    private async Task RunLoopAsync(PeriodicTimer timer)
    {
        // Publish immediately on start, then on every tick. Each pass is guarded: a transient failure (a
        // manager not ready at boot, an ECP blip) must be logged and retried, never fault the loop task.
        await TryRefreshAsync().ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            await TryRefreshAsync().ConfigureAwait(false);
        }
    }

    private async Task TryRefreshAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // One bad pass must not stop all future ticks (mirrors PlaybackReaperService's sweep guard).
            _logger.LogError(ex, "[JellyRock] cold-launch phantom refresh failed");
        }
    }

    private async Task RefreshAsync()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        // Snapshot the pairings so a concurrent /pair report (which replaces the collection contents) can't
        // mutate what we iterate.
        var pairings = plugin.Configuration.Pairings.ToList();

        // Snapshot the admin toggles for this pass (issue #668). Flipping either off makes ShouldPublish
        // fail below, so the existing revoke path drops the phantom on this same tick — no extra teardown.
        var coldCastEnabled = plugin.Configuration.EnableColdLaunchCasting;
        var includeDevBuilds = plugin.Configuration.IncludeDevelopmentBuilds;

        foreach (var record in pairings)
        {
            await RefreshPairingAsync(record, coldCastEnabled, includeDevBuilds, now).ConfigureAwait(false);
        }
    }

    private async Task RefreshPairingAsync(PairingRecord record, bool coldCastEnabled, bool includeDevBuilds, DateTime now)
    {
        var existing = FindSession(record.JellyfinDeviceId);
        var appIsOpen = existing is not null && IsAppOpen(existing);

        // Admin gate (issue #668): the master toggle plus the dev-build filter. When it disallows this
        // pairing, short-circuit so the live ECP re-probe below never fires for a disabled target.
        var configAllows = PairingDecision.ConfigAllowsPublish(record, coldCastEnabled, includeDevBuilds);

        // Re-probe the validated wake address live so a Roku that has powered off / left the LAN drops off,
        // even though the persisted record is still validated + fresh. Skipped when the app is open (the live
        // session owns the presence then), when config disallows publishing, and when there is nothing to advertise.
        var reachableNow = configAllows
            && !appIsOpen
            && PairingDecision.IsAdvertisable(record, now, PairingDecision.FreshnessWindow)
            && await _pairingValidation.FindReachableWakeIpAsync(new[] { record.WakeIp }, CancellationToken.None).ConfigureAwait(false) is not null;

        if (!configAllows || !PairingDecision.ShouldPublish(record, appIsOpen, reachableNow, now, PairingDecision.FreshnessWindow))
        {
            // Revoke: flip any attached phantom controller off so the device drops from the cast list. Do NOT
            // touch capabilities or refresh activity — when the app is open that would stomp its live session,
            // and when the Roku is gone we want the session to idle-expire.
            existing?.SessionControllers.OfType<PhantomSessionController>().FirstOrDefault()?.SetLive(false);
            return;
        }

        var userId = ParseUserId(record.UserId);
        var user = userId is { } id ? _userManager.GetUserById(id) : null;
        if (user is null)
        {
            _logger.LogWarning("[JellyRock] cold-launch pairing for {DeviceId} names an unknown user — cannot own the phantom", record.JellyfinDeviceId);
            return;
        }

        var deviceName = string.IsNullOrWhiteSpace(existing?.DeviceName) ? DefaultDeviceName(record) : existing!.DeviceName;
        var appVersion = string.IsNullOrWhiteSpace(existing?.ApplicationVersion) ? PhantomAppVersion : existing!.ApplicationVersion;

        // Mint (or refresh) the phantom. Keyed by device+client+user, so this resolves to the same SessionInfo
        // the app uses when open — one continuous identity.
        var session = await _sessionManager.LogSessionActivity(
            JellyRockSessionService.JellyRockClientName,
            appVersion,
            record.JellyfinDeviceId,
            deviceName,
            record.WakeIp,
            user).ConfigureAwait(false);

        // Advertise the CLOSED-state capability set: SupportsMediaControl keeps it in the picker, the reduced
        // SupportedCommands=[DisplayContent] makes the web render only the wake affordance (no no-op transport
        // controls), PlayableMediaTypes lets Play be offered.
        session.Capabilities ??= new ClientCapabilities();
        session.Capabilities.SupportsMediaControl = true;
        session.Capabilities.PlayableMediaTypes = new[] { MediaType.Video, MediaType.Audio };
        session.Capabilities.SupportedCommands = new[] { GeneralCommandType.DisplayContent };

        var ensured = session.EnsureController<PhantomSessionController>(
            s => new PhantomSessionController(s, _httpClientFactory, _logger));
        var controller = (PhantomSessionController)ensured.Item1;
        controller.SetLive(true);
        if (ensured.Item2)
        {
            _sessionManager.OnSessionControllerConnected(session);
            _logger.LogInformation(
                "[JellyRock] published cold-launch phantom: DeviceId={DeviceId} name='{Name}' wakeIp={WakeIp} appId={AppId}",
                record.JellyfinDeviceId,
                deviceName,
                record.WakeIp,
                record.AppId);
        }
    }

    private SessionInfo? FindSession(string deviceId)
    {
        return _sessionManager.Sessions.FirstOrDefault(s =>
            string.Equals(s.Client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    // The pairing stores UserId as an N-format guid (see RemoteControlController.Pair). Parse defensively —
    // a malformed value must skip the pairing, not throw and stop the whole refresh.
    private static Guid? ParseUserId(string userId)
    {
        return Guid.TryParseExact(userId, "N", out var id) ? id : null;
    }

    // Cosmetic name used only when the app has never reported a real DeviceName for this device. Distinguish
    // dev sideloads so a developer's target reads differently from a channel install.
    private static string DefaultDeviceName(PairingRecord record)
    {
        return record.IsDev ? "JellyRock (dev)" : "JellyRock";
    }
}
