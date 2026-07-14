using System;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteControlController"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    public RemoteControlController(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
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
    /// </summary>
    /// <param name="waitMs">Requested hold ceiling in ms (clamped server-side to 5-30s).</param>
    /// <param name="cancellationToken">Request cancellation (client disconnect).</param>
    /// <returns>200 with a JSON array of command envelopes, or 204 if the hold elapsed empty.</returns>
    [HttpGet("poll")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Poll([FromQuery] int waitMs = MaxWaitMs, CancellationToken cancellationToken = default)
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

        var batch = await controller.DequeueBatchAsync(waitMs, cancellationToken).ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return NoContent();
        }

        return Ok(batch);
    }
}
