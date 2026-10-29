using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Stands in for Jellyfin's native WebSocketController (Emby.Server.Implementations, not referenceable
/// from a plugin): an <see cref="ISessionController"/> whose liveness we control, to model the HTTP open state.
/// </summary>
internal sealed class FakeNativeController : ISessionController
{
    private readonly bool _isActive;

    public FakeNativeController(bool isActive) => _isActive = isActive;

    public bool IsSessionActive => _isActive;

    public bool SupportsMediaControl => _isActive;

    public Task SendMessage<T>(SessionMessageType name, Guid messageId, T data, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
