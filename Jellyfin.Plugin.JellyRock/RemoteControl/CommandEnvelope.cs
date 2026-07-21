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
    /// Gets the server-assigned message id — the ack key for the opt-in at-least-once delivery
    /// (contract v1). An ack-capable client echoes the last id it durably received back as the poll's
    /// cumulative <c>ackId</c>, which lets the plugin drop confirmed commands and redeliver the rest;
    /// the client also dedupes by this id. A legacy client ignores it (at-most-once). See
    /// <see cref="QueueingSessionController.DequeueBatchAsync"/> and the wire contract.
    /// </summary>
    public Guid MessageId { get; init; }
}
