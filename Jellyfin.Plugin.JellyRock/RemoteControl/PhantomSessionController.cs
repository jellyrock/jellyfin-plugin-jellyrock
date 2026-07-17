using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The <see cref="ISessionController"/> attached to a cold-launch <b>phantom</b> session (issue #668,
/// P2) — the closed→open bridge. Two jobs:
/// <list type="number">
///   <item>Carry the phantom's liveness: <see cref="IsSessionActive"/> / <see cref="SupportsMediaControl"/>
///     report a flag the owning <see cref="PhantomSessionService"/> sets each tick from pairing validity +
///     a live reachability re-probe. Unlike the shipped <see cref="QueueingSessionController"/> (alive iff a
///     poll is fresh — which a closed app never sends), this liveness is driven by the service's clock, so a
///     closed-but-reachable Roku stays advertised and a powered-off one drops off within a tick.</item>
///   <item>On a wake command, fire ECP <c>POST /launch/&lt;appId&gt;?contentId=…</c> at the paired Roku to
///     cold-wake it into the item, reusing JellyRock's deep-link contract verbatim
///     (<c>id=&lt;itemId&gt;|serverId=&lt;serverGuid&gt;|action=&lt;verb&gt;</c>). Only two commands wake the app —
///     Play (<c>action=play</c>) and DisplayContent (<c>action=open</c>); every other command is meaningless
///     for a closed app and dropped.</item>
/// </list>
///
/// <para>Self-gating: the server fans a command to <b>every</b> controller on a session regardless of its
/// advertised capabilities, so when the app is OPEN this controller still receives the command — where the
/// live <see cref="QueueingSessionController"/> long-poll already delivers it. Firing ECP then would
/// re-<c>/launch</c> an already-running app (disruptive). So the wake is suppressed whenever a live
/// <see cref="QueueingSessionController"/> is attached (<see cref="PhantomSessionService.IsAppOpen"/>); this
/// needs no controller detach and stays correct through the open/closed transition.</para>
/// </summary>
public sealed class PhantomSessionController : ISessionController
{
    // String enums so a GeneralCommand's Name serializes as "DisplayContent" (not its integer value),
    // matching how it is detected below and how the JellyRock client parses it (mirrors
    // QueueingSessionController.SerializerOptions).
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SessionInfo _session;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    // Set by PhantomSessionService each tick: true while the pairing is advertisable and the Roku still
    // answers an ECP re-probe, false to revoke the phantom (Roku off / paired-out). Volatile because the
    // session-manager reads the getters on any /Sessions query, off the service's timer thread.
    private volatile bool _live;

    /// <summary>Initializes a new instance of the <see cref="PhantomSessionController"/> class.</summary>
    /// <param name="session">The phantom session this controller drives.</param>
    /// <param name="httpClientFactory">Factory for the ECP HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public PhantomSessionController(SessionInfo session, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _session = session;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsSessionActive => _live;

    /// <inheritdoc />
    public bool SupportsMediaControl => _live;

    /// <summary>
    /// Sets whether the phantom is currently advertised as controllable. Called by
    /// <see cref="PhantomSessionService"/> each tick — true to publish/keep the cast target, false to revoke
    /// it (so <c>SessionInfo.SupportsRemoteControl</c> recomputes false on the next cast-list query and the
    /// device drops off). Idempotent.
    /// </summary>
    /// <param name="live">Whether the phantom should be advertised.</param>
    public void SetLive(bool live) => _live = live;

    /// <inheritdoc />
    public Task SendMessage<T>(SessionMessageType name, Guid messageId, T data, CancellationToken cancellationToken)
    {
        // If the app is open, the live QueueingSessionController long-poll already delivers this command;
        // firing ECP would re-launch a running app. Suppress and let the live path own it.
        if (PhantomSessionService.IsAppOpen(_session))
        {
            return Task.CompletedTask;
        }

        // Serialize once so ItemIds can be pulled out of a Play/DisplayContent without a hard compile-time
        // dependency on the request shapes.
        JsonElement payload;
        try
        {
            payload = JsonSerializer.SerializeToElement(data, SerializerOptions);
        }
        catch (NotSupportedException)
        {
            return Task.CompletedTask; // unserializable — cannot carry an item to wake into
        }

        string? wakeItemId = null;
        string? wakeAction = null;

        if (name == SessionMessageType.Play && TryGetFirstItemId(payload, out var playId))
        {
            wakeItemId = playId;
            wakeAction = "play";
        }
        else if (name == SessionMessageType.GeneralCommand && TryGetDisplayContentItemId(payload, out var openId))
        {
            wakeItemId = openId;
            wakeAction = "open";
        }

        if (wakeItemId is not null)
        {
            // Fire-and-forget: the server's command fan-out must not block on the wake round-trip.
            _ = WakeAsync(wakeItemId, wakeAction!, cancellationToken);
        }

        return Task.CompletedTask;
    }

    // GeneralCommand DisplayContent: { "Name": "DisplayContent", "Arguments": { "ItemId": "...", ... } }.
    private static bool TryGetDisplayContentItemId(JsonElement payload, out string itemId)
    {
        itemId = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("Name", out var nameProp)
            || nameProp.ValueKind != JsonValueKind.String
            || !string.Equals(nameProp.GetString(), "DisplayContent", StringComparison.OrdinalIgnoreCase)
            || !payload.TryGetProperty("Arguments", out var args)
            || args.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var arg in args.EnumerateObject())
        {
            if (string.Equals(arg.Name, "ItemId", StringComparison.OrdinalIgnoreCase)
                && arg.Value.ValueKind == JsonValueKind.String)
            {
                itemId = arg.Value.GetString() ?? string.Empty;
                return !string.IsNullOrEmpty(itemId);
            }
        }

        return false;
    }

    // PlayRequest serializes ItemIds as a Guid array; take the first (single-item cast, per the #666 limit).
    private static bool TryGetFirstItemId(JsonElement payload, out string itemId)
    {
        itemId = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var prop in payload.EnumerateObject())
        {
            if (!string.Equals(prop.Name, "ItemIds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
            {
                itemId = prop.Value[0].GetString() ?? string.Empty;
                return !string.IsNullOrEmpty(itemId);
            }
        }

        return false;
    }

    private async Task WakeAsync(string itemId, string action, CancellationToken cancellationToken)
    {
        // Read the freshest pairing for this device at fire time: the Roku's LAN address can change
        // between reports, and the record is re-sent on every app open, so the config store is the truth.
        var pairing = Plugin.Instance?.Configuration.Pairings
            .FirstOrDefault(p => string.Equals(p.JellyfinDeviceId, _session.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (pairing is null || string.IsNullOrWhiteSpace(pairing.WakeIp))
        {
            _logger.LogWarning("[JellyRock] cold-launch wake for {DeviceId} has no reachable pairing — cannot wake", _session.DeviceId);
            return;
        }

        var appId = string.IsNullOrWhiteSpace(pairing.AppId) ? "dev" : pairing.AppId;
        var serverId = _session.ServerId;

        // Reuse JellyRock's deep-link contract verbatim (source/replayRoute.bs parseDeepLinkContentId).
        var contentId = string.Create(
            CultureInfo.InvariantCulture,
            $"id={itemId}|serverId={serverId}|action={action}");
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"http://{pairing.WakeIp}:8060/launch/{appId}?contentId={Uri.EscapeDataString(contentId)}");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            // ECP /launch is an HTTP POST with an empty body.
            using var content = new StringContent(string.Empty);
            using var response = await client.PostAsync(new Uri(url), content, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "[JellyRock] cold-launch ECP wake POST {Url} -> {Status}",
                url,
                (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "[JellyRock] cold-launch ECP wake POST to {Url} failed", url);
        }
    }
}
