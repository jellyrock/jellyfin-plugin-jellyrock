using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.JellyRock.RemoteControl;

/// <summary>
/// The <see cref="ISessionController"/> attached to a JellyRock session on an HTTPS server. The
/// server fans every Play / Playstate / GeneralCommand for the session out to
/// <see cref="SendMessage{T}"/>; this controller queues them for the long-poll endpoint to drain.
///
/// <para>Liveness (the closed-app fix): <see cref="SupportsMediaControl"/> and
/// <see cref="IsSessionActive"/> are computed live from the last poll. Jellyfin's
/// <c>SessionInfo.SupportsRemoteControl</c> — what the web cast list filters on — requires BOTH the
/// session capability flag AND a controller reporting <c>SupportsMediaControl</c>, and it is
/// recomputed on every <c>/Sessions</c> query. So when JellyRock stops polling (app closed, poll
/// loop dead) and the grace window lapses, the next cast-list query recomputes false and JellyRock
/// drops out — no background sweep, no disconnect callback needed. The ws:// path gets this from a
/// socket disconnect; here the absence of a fresh poll IS the disconnect.</para>
/// </summary>
public sealed class QueueingSessionController : ISessionController
{
    // Ceiling on unpolled commands held for one session. Remote-control commands are tiny and polled
    // within seconds, so this is generous; DropOldest keeps the NEWEST commands if a client somehow
    // stops draining (a dead client is about to lapse out of the cast list anyway).
    private const int QueueCapacity = 256;

    // Serialize command payloads with STRING enums so the wire matches what Jellyfin's session
    // WebSocket sends and what the JellyRock client parses (playstateVerb / GeneralCommand.Name look
    // up string values like "Pause" / "NextTrack" / "DisplayContent"). The System.Text.Json default
    // emits enums as INTEGERS, which the client can't match — so without this every enum-carrying
    // command (Playstate, GeneralCommand nav) silently falls through to "ignore". Play survives only
    // because its action defaults sensibly and rides on ItemIds.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Bounded + multi-reader: DropOldest bounds memory if a client stops draining; SingleReader is
    // deliberately NOT set, so two briefly-overlapping polls for one session (an abandoned-but-still
    // -held request racing the client's retry) can't violate a single-reader contract.
    private readonly Channel<CommandEnvelope> _queue =
        Channel.CreateBounded<CommandEnvelope>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    // UtcNow.Ticks after which this controller is stale. 0 (never polled) reads as stale, so a session
    // is never advertised as controllable before it actually polls. Stored as a single value (not
    // lastPoll + grace separately) so a concurrent read can't observe a torn lastPoll/grace pair.
    private long _freshUntilTicks;

    /// <inheritdoc />
    public bool IsSessionActive => IsPollFresh();

    /// <inheritdoc />
    public bool SupportsMediaControl => IsPollFresh();

    /// <summary>
    /// Records a poll and refreshes the liveness window. The window is <c>2 × waitMs</c> — long enough
    /// that a healthy client (which re-polls every <c>waitMs</c>) never flaps out between polls, short
    /// enough that a closed app drops from the cast list within ~2 poll cycles. Derived from the
    /// client's own requested hold, so there is no tuning knob to misconfigure.
    /// </summary>
    /// <param name="waitMs">The poll's requested hold ceiling, in milliseconds (already clamped by the caller).</param>
    public void MarkPolled(int waitMs)
    {
        var graceTicks = TimeSpan.FromMilliseconds(2L * waitMs).Ticks;
        Interlocked.Exchange(ref _freshUntilTicks, DateTime.UtcNow.Ticks + graceTicks);
    }

    /// <inheritdoc />
    public Task SendMessage<T>(SessionMessageType name, Guid messageId, T data, CancellationToken cancellationToken)
    {
        // The poll request itself is the keepalive (it refreshes session activity), so keepalive
        // frames are never forwarded on this channel (contract v1).
        if (name == SessionMessageType.KeepAlive || name == SessionMessageType.ForceKeepAlive)
        {
            return Task.CompletedTask;
        }

        // Pre-serialize to a JsonElement so the payload is captured by its concrete runtime type (a
        // boxed `object` property would risk serializing empty), with string enums (see above).
        JsonElement payload;
        try
        {
            payload = JsonSerializer.SerializeToElement(data, SerializerOptions);
        }
        catch (NotSupportedException)
        {
            return Task.CompletedTask; // an unserializable command can't be delivered anyway
        }

        _queue.Writer.TryWrite(new CommandEnvelope
        {
            MessageType = name.ToString(),
            Data = payload,
            MessageId = messageId
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Long-poll drain: returns any already-queued commands immediately; otherwise waits up to
    /// <paramref name="waitMs"/> for the first command, then drains everything available. An empty
    /// result means the hold window elapsed with nothing queued (the endpoint returns 204).
    ///
    /// <para>At-most-once in v1: a returned batch is removed from the queue before the response body
    /// is written, so a client that disconnects between drain and delivery loses that batch (the
    /// user re-issues). The reserved <see cref="CommandEnvelope.MessageId"/> is the hook for a future
    /// at-least-once ack — see the wire contract.</para>
    /// </summary>
    /// <param name="waitMs">Maximum hold time in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation (e.g. the client disconnecting).</param>
    /// <returns>The commands to deliver, in FIFO order (possibly empty).</returns>
    public async Task<IReadOnlyList<CommandEnvelope>> DequeueBatchAsync(int waitMs, CancellationToken cancellationToken)
    {
        var batch = new List<CommandEnvelope>();
        while (_queue.Reader.TryRead(out var queued))
        {
            batch.Add(queued);
        }

        if (batch.Count > 0)
        {
            return batch;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(waitMs);
        try
        {
            if (await _queue.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var queued))
                {
                    batch.Add(queued);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Hold window elapsed (or client disconnected) with nothing queued -> empty batch.
        }

        return batch;
    }

    private bool IsPollFresh() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _freshUntilTicks);
}
