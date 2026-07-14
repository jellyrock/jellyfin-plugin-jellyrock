using System;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// One queued remote-control command, serialized to JellyRock in the long-poll response array.
/// Deliberately mirrors the <c>{ MessageType, Data }</c> shape Jellyfin pushes over the session
/// WebSocket so the JellyRock client's existing <c>remoteCommand.parseMessage</c> consumes it
/// unchanged. See docs/architecture/remote-control-longpoll-contract.md in the JellyRock repo.
/// </summary>
public sealed class CommandEnvelope
{
    /// <summary>
    /// Gets the message type (e.g. <c>Play</c>, <c>Playstate</c>, <c>GeneralCommand</c>) — the
    /// <see cref="MediaBrowser.Model.Session.SessionMessageType"/> name the server fanned out.
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the command payload, verbatim from the server's controller fan-out (a PlayRequest /
    /// PlaystateRequest / GeneralCommand). Serialized inline so the client sees the identical Data
    /// object it would have received over the WebSocket.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Gets the server-assigned message id. Reserved for a future at-least-once ack; the client does
    /// not ack in contract v1.
    /// </summary>
    public Guid MessageId { get; init; }
}
