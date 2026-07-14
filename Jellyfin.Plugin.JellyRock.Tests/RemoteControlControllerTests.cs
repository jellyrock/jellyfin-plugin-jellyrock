using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Tests for <see cref="RemoteControlController"/> — the authorization / identity-resolution branches
/// that gate who may drive the long-poll channel. These are the security-relevant paths (an authenticated
/// non-JellyRock client must not be able to poll, and only the caller's own device resolves a session),
/// so they are pinned by tests independently of the queue/liveness unit that
/// <see cref="QueueingSessionControllerTests"/> covers.
/// </summary>
public class RemoteControlControllerTests
{
    // Jellyfin auth claim types (mirrored from Jellyfin.Api, which plugins can't reference — same as the
    // controller under test).
    private const string DeviceIdClaim = "Jellyfin-DeviceId";
    private const string ClientClaim = "Jellyfin-Client";

    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();

    [Fact]
    public async Task Poll_MissingClaims_ReturnsUnauthorized()
    {
        _sessionManager.Sessions.Returns(Array.Empty<SessionInfo>());
        var controller = BuildController(); // no claims

        var result = await controller.Poll(cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Poll_NonJellyRockClient_ReturnsForbid()
    {
        _sessionManager.Sessions.Returns(Array.Empty<SessionInfo>());
        var controller = BuildController(
            new Claim(DeviceIdClaim, "device-1"),
            new Claim(ClientClaim, "Jellyfin Web"));

        var result = await controller.Poll(cancellationToken: CancellationToken.None);

        // An authenticated but non-JellyRock client must never drive the channel.
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Poll_NoMatchingSession_ReturnsNotFound()
    {
        // Correct client, but no session for this device (e.g. the poll raced session teardown).
        _sessionManager.Sessions.Returns(Array.Empty<SessionInfo>());
        var controller = BuildController(
            new Claim(DeviceIdClaim, "device-1"),
            new Claim(ClientClaim, JellyRockSessionService.JellyRockClientName));

        var result = await controller.Poll(cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Poll_MatchingSession_ForcesCapability_AndReturnsQueuedCommands()
    {
        var session = BuildSession("device-1", JellyRockSessionService.JellyRockClientName);
        _sessionManager.Sessions.Returns(new[] { session });

        // Pre-attach and queue a command so the poll drains immediately (no hold delay).
        var attached = JellyRockSessionService.EnsureAttached(_sessionManager, session);
        await attached.SendMessage(SessionMessageType.Playstate, Guid.NewGuid(), new { Command = "Pause" }, CancellationToken.None);

        var controller = BuildController(
            new Claim(DeviceIdClaim, "device-1"),
            new Claim(ClientClaim, JellyRockSessionService.JellyRockClientName));

        var result = await controller.Poll(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var batch = Assert.IsAssignableFrom<IReadOnlyList<CommandEnvelope>>(ok.Value);
        Assert.Single(batch);
        // The poll must have forced the media-control capability the secure path depends on.
        Assert.True(session.Capabilities!.SupportsMediaControl);
    }

    [Fact]
    public async Task Poll_MatchingSession_EmptyQueue_ReturnsNoContent()
    {
        var session = BuildSession("device-1", JellyRockSessionService.JellyRockClientName);
        _sessionManager.Sessions.Returns(new[] { session });

        var controller = BuildController(
            new Claim(DeviceIdClaim, "device-1"),
            new Claim(ClientClaim, JellyRockSessionService.JellyRockClientName));

        // A pre-cancelled token makes the empty hold return immediately instead of waiting out the clamp.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await controller.Poll(cancellationToken: cts.Token);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public void GetInfo_ReturnsContractVersion()
    {
        var controller = BuildController();

        var ok = Assert.IsType<OkObjectResult>(controller.GetInfo());
        Assert.NotNull(ok.Value);
    }

    private RemoteControlController BuildController(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new RemoteControlController(_sessionManager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    private SessionInfo BuildSession(string deviceId, string client) =>
        new(_sessionManager, Substitute.For<ILogger>())
        {
            DeviceId = deviceId,
            Client = client,
            Capabilities = new ClientCapabilities { SupportsMediaControl = false }
        };
}
