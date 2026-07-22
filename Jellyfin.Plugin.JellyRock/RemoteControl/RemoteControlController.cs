using System;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The HTTPS long-poll command channel JellyRock consumes over TLS (Roku can't ws://). Auto-registered
/// as an MVC ApplicationPart by Jellyfin's plugin loader. See the frozen wire contract in the
/// JellyRock repo: docs/architecture/remote-control-longpoll-contract.md.
/// </summary>
[ApiController]
[Route("JellyRock/RemoteControl")]
[Produces(MediaTypeNames.Application.Json)]
public class RemoteControlController : ControllerBase
{
    /// <summary>The wire-contract version this plugin speaks.</summary>
    private const int ContractVersion = 1;

    // Poll hold band. The client requests a hold via waitMs; the server clamps it so a buggy/hostile
    // client can't request a tiny hold (request storm) or a huge one (the liveness window is 2xwaitMs,
    // so an unbounded waitMs would keep a dead session advertised too long). 5-30s keeps the derived
    // grace in a sane ~10-60s band.
    private const int MinWaitMs = 5000;
    private const int MaxWaitMs = 30000;

    // Jellyfin auth claim types (stable strings; the constants themselves live in the non-plugin
    // Jellyfin.Api assembly, so they are mirrored here).
    private const string DeviceIdClaim = "Jellyfin-DeviceId";
    private const string ClientClaim = "Jellyfin-Client";

    private readonly ISessionManager _sessionManager;
    private readonly PairingValidationService _pairingValidation;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteControlController"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="pairingValidation">The cold-launch pairing reachability probe.</param>
    public RemoteControlController(ISessionManager sessionManager, PairingValidationService pairingValidation)
    {
        _sessionManager = sessionManager;
        _pairingValidation = pairingValidation;
    }

    /// <summary>
    /// Presence + version probe. JellyRock hits this on an https server to decide whether to run the
    /// long-poll transport; a 404 (plugin absent) tells it to stay dark.
    /// </summary>
    /// <returns>The contract + plugin version.</returns>
    [HttpGet("info")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult GetInfo()
    {
        return Ok(new
        {
            ContractVersion,
            PluginVersion = Plugin.Instance?.Version?.ToString() ?? "0.0.0.0"
        });
    }

    /// <summary>
    /// Long-poll for queued remote-control commands. Holds up to <paramref name="waitMs"/> for a
    /// command, refreshing session activity and liveness as a side effect (the poll IS the keepalive).
    ///
    /// <para><paramref name="ack"/>/<paramref name="ackId"/> are the additive, opt-in at-least-once
    /// signal (contract v1). An ack-capable client sends <c>ack=1</c> on every poll and, once it has
    /// received a command, <c>ackId=&lt;last MessageId it durably received&gt;</c>; the plugin then
    /// retains delivered commands and redelivers any the client hasn't confirmed. A client that omits
    /// them gets the legacy at-most-once drain — so this never regresses an older client.</para>
    /// </summary>
    /// <param name="waitMs">Requested hold ceiling in ms (clamped server-side to 5-30s).</param>
    /// <param name="ack">1 if the client is ack-capable (retain + redeliver until acked); absent/0 for the legacy at-most-once drain.</param>
    /// <param name="ackId">The client's cumulative ack — the last <c>MessageId</c> it durably received. Absent/unparseable acks nothing. Only meaningful with <c>ack=1</c>.</param>
    /// <param name="cancellationToken">Request cancellation (client disconnect).</param>
    /// <returns>200 with a JSON array of command envelopes, or 204 if the hold elapsed empty.</returns>
    [HttpGet("poll")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Poll([FromQuery] int waitMs = MaxWaitMs, [FromQuery] int ack = 0, [FromQuery] string? ackId = null, CancellationToken cancellationToken = default)
    {
        var deviceId = User.FindFirst(DeviceIdClaim)?.Value;
        var client = User.FindFirst(ClientClaim)?.Value;
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(client))
        {
            return Unauthorized();
        }

        // Only a JellyRock session may drive the channel. Resolve THIS caller's own session by its
        // authenticated device + client (no query parameter is trusted for identity).
        if (!string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var session = _sessionManager.Sessions.FirstOrDefault(s =>
            string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Client, client, StringComparison.OrdinalIgnoreCase));

        if (session is null)
        {
            return NotFound();
        }

        // Refresh activity (keeps the session inside activeWithinSeconds windows) and ensure the
        // queueing controller is attached (fallback if the poll raced the session event).
        session.LastActivityDate = DateTime.UtcNow;
        waitMs = Math.Clamp(waitMs, MinWaitMs, MaxWaitMs);
        var controller = JellyRockSessionService.EnsureAttached(_sessionManager, session);
        controller.MarkPolled(waitMs);

        // Opt-in at-least-once: ack=1 flags an ack-capable client; ackId (if a valid GUID) is its
        // cumulative ack. An absent/garbled ackId acks nothing (Guid.Empty), so the buffer is retained.
        var ackMode = ack == 1;
        _ = Guid.TryParse(ackId, out var parsedAckId);

        var batch = await controller.DequeueBatchAsync(waitMs, ackMode, parsedAckId, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return NoContent();
        }

        return Ok(batch);
    }

    /// <summary>
    /// Cold-launch pairing report (issue #668). A JellyRock client posts its LAN addresses + ECP app
    /// identity so the server can wake it via ECP <c>/launch</c> while the app is closed. Identity is
    /// bound from the caller's authenticated session (never the body), the reported addresses are probed
    /// to find one the server can reach, and the validated pairing is persisted. Fire-and-forget on the
    /// client side, so the response body is only a courtesy status.
    /// </summary>
    /// <remarks>
    /// Contract note: <c>/pair</c> is intentionally version-FREE, unlike <c>/info</c>+<c>/poll</c> which
    /// carry <see cref="ContractVersion"/>. It is a registration, not a command, so its cross-version
    /// safety is THIS HTTP status contract, not a version field. A BREAKING change to the request shape
    /// MUST reject older clients with 400 (or move the route so they 404). NEVER silently reinterpret a
    /// field: the client is fire-and-forget and never reads this response, so a misparse would fire an
    /// ECP wake at a wrong or garbage target with no signal. Additive changes stay safe, since unknown
    /// request fields are ignored on deserialize and identity is auth-claim-bound. Mirrors the
    /// client-side note in JellyRock's remoteProtocol.bs and docs/architecture/remote-control.md.
    /// </remarks>
    /// <param name="request">The pairing report body (addresses + app identity).</param>
    /// <param name="cancellationToken">Request cancellation (client disconnect).</param>
    /// <returns>200 with the validation status, or an error status.</returns>
    [HttpPost("pair")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Pair([FromBody] PairRequest request, CancellationToken cancellationToken = default)
    {
        var deviceId = User.FindFirst(DeviceIdClaim)?.Value;
        var client = User.FindFirst(ClientClaim)?.Value;
        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(client))
        {
            return Unauthorized();
        }

        // Only a JellyRock session may register a JellyRock cast target (identity from the claim, never a body field).
        if (!string.Equals(client, JellyRockSessionService.JellyRockClientName, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        if (request?.RokuIps is null || request.RokuIps.Count == 0 || string.IsNullOrWhiteSpace(request.AppId))
        {
            return BadRequest();
        }

        // Resolve THIS caller's live session (it just reported, so it is open) — the trusted source of the
        // owning user and the endpoint the server saw the device connect from.
        var session = _sessionManager.Sessions.FirstOrDefault(s =>
            string.Equals(s.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Client, client, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var wakeIp = await _pairingValidation.FindReachableWakeIpAsync(request.RokuIps, cancellationToken).ConfigureAwait(false);
        var validated = wakeIp is not null;

        var record = new PairingRecord
        {
            JellyfinDeviceId = deviceId,
            UserId = session.UserId.ToString("N", CultureInfo.InvariantCulture),
            AppId = request.AppId,
            IsDev = request.IsDev,
            RemoteEndPoint = session.RemoteEndPoint ?? string.Empty,
            WakeIp = wakeIp ?? string.Empty,
            Validated = validated,
            LastValidated = validated ? now : default,
            LastSeen = now
        };

        PairingStore.Upsert(record, now);
        return Ok(new { Validated = validated });
    }
}
