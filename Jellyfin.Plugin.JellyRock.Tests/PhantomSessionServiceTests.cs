using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Unit tests for <see cref="PhantomSessionService.IsAppOpen"/> — the single, <b>transport-agnostic</b>
/// "is the app open?" signal that both the publish guard and the ECP-wake suppression key off. The
/// behavior under test is that "open" spans BOTH remote-control transports (issue #668 HTTP fix): a
/// poll-fresh <see cref="QueueingSessionController"/> on HTTPS, OR a live native <c>ws://</c> controller
/// on HTTP — while a published phantom (a <em>closed</em> app) must never read as open.
/// </summary>
public class PhantomSessionServiceTests
{
    [Fact]
    public void IsAppOpen_False_WhenNoControllers()
    {
        var session = BuildSession();

        Assert.False(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_False_WhenOnlyPhantomIsLive()
    {
        // A published phantom advertises a CLOSED app: its controller reports IsSessionActive while live.
        // Counting it would read every published cast target as "open" and defeat the whole feature.
        var session = BuildSession();
        AttachLivePhantom(session);

        Assert.False(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_True_WhenLongPollControllerIsPollFresh_Https()
    {
        var session = BuildSession();
        session.EnsureController<QueueingSessionController>(_ => PollFreshLongPoll());

        Assert.True(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_False_WhenLongPollControllerIsStale_Https()
    {
        // A QueueingSessionController that never polled (or lapsed) reads inactive — a closed HTTPS app.
        var session = BuildSession();
        session.EnsureController<QueueingSessionController>(_ => new QueueingSessionController());

        Assert.False(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_True_WhenNativeWebSocketControllerIsLive_Http()
    {
        // On HTTP the open app never polls the long-poll; it rides Jellyfin's native ws:// controller,
        // whose IsSessionActive tracks a live socket (verified against WebSocketController @ v10.11.11).
        // The stub stands in for that native controller — any live non-phantom controller means "open".
        var session = BuildSession();
        session.EnsureController<FakeNativeController>(_ => new FakeNativeController(isActive: true));

        Assert.True(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_False_WhenNativeWebSocketControllerIsClosed_Http()
    {
        // Socket closed: the native controller lingers on the session but reports inactive — a closed HTTP app.
        var session = BuildSession();
        session.EnsureController<FakeNativeController>(_ => new FakeNativeController(isActive: false));

        Assert.False(PhantomSessionService.IsAppOpen(session));
    }

    [Fact]
    public void IsAppOpen_True_WhenAppOpenAlongsideALivePhantom()
    {
        // During the closed->open handoff both may be attached at once. A live non-phantom controller
        // wins: the app is open, so the phantom must back off (revoke + suppress the ECP wake).
        var session = BuildSession();
        AttachLivePhantom(session);
        session.EnsureController<FakeNativeController>(_ => new FakeNativeController(isActive: true));

        Assert.True(PhantomSessionService.IsAppOpen(session));
    }

    private static SessionInfo BuildSession() =>
        new(Substitute.For<ISessionManager>(), Substitute.For<ILogger>())
        {
            DeviceId = "device-1",
            Client = "JellyRock"
        };

    private static void AttachLivePhantom(SessionInfo session)
    {
        var ensured = session.EnsureController<PhantomSessionController>(
            s => new PhantomSessionController(s, Substitute.For<IHttpClientFactory>(), Substitute.For<ILogger>()));
        ((PhantomSessionController)ensured.Item1).SetLive(true);
    }

    private static QueueingSessionController PollFreshLongPoll()
    {
        var controller = new QueueingSessionController();
        controller.MarkPolled(25000);
        return controller;
    }

    // Stands in for Jellyfin's native WebSocketController (Emby.Server.Implementations, not referenceable
    // from a plugin): an ISessionController whose liveness we control, to model the HTTP open state.
    private sealed class FakeNativeController : ISessionController
    {
        private readonly bool _isActive;

        public FakeNativeController(bool isActive) => _isActive = isActive;

        public bool IsSessionActive => _isActive;

        public bool SupportsMediaControl => _isActive;

        public Task SendMessage<T>(SessionMessageType name, Guid messageId, T data, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
