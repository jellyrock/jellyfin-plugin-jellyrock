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

    // At-least-once (contract v1, opt-in): commands drained for an ack-capable client are held here —
    // delivered-but-unacked — instead of being dropped, so a poll whose response is lost redelivers
    // them on the next poll (keyed by the client's cumulative ackId). Guarded by _ackGate because,
    // unlike the lock-free channel, this ordered buffer is read-modify-written per poll and two
    // overlapping polls must see it consistently. Capped like the channel: an ack-capable client that
    // receives but never acks is buggy/gone, so DropOldest (keep NEWEST) bounds the memory. Legacy
    // (non-ack) clients never touch this buffer — their path stays exactly the at-most-once drain.
    private readonly object _ackGate = new();
    private readonly List<CommandEnvelope> _unacked = new();

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
    /// <para>Delivery guarantee is chosen by <paramref name="ackMode"/>, an additive, opt-in extension
    /// of contract v1 (see the wire contract):</para>
    /// <list type="bullet">
    /// <item><description><b>Legacy (<c>ackMode=false</c>) — at-most-once.</b> The batch is removed from
    /// the queue before the response body is written, so a client that disconnects between drain and
    /// delivery loses that batch (the user re-issues). This is exactly the pre-ack behavior; a client
    /// that doesn't send the ack signal keeps it, so a new plugin never regresses an old client.</description></item>
    /// <item><description><b>Ack-capable (<c>ackMode=true</c>) — at-least-once.</b> First the buffer is
    /// pruned through the client's cumulative <paramref name="ackId"/> ("I durably received through this
    /// <see cref="CommandEnvelope.MessageId"/>"); then newly-queued commands are appended; then the whole
    /// unacked buffer is returned — so anything the client hasn't confirmed is <em>redelivered</em> on the
    /// next poll. A lost response therefore self-heals. The client must dedupe by <c>MessageId</c> (a
    /// redelivered relative verb like <c>NextTrack</c> would otherwise double-apply) — see the contract.</description></item>
    /// </list>
    /// </summary>
    /// <param name="waitMs">Maximum hold time in milliseconds.</param>
    /// <param name="ackMode">True if the caller is ack-capable (retain + redeliver until acked); false for the legacy at-most-once drain.</param>
    /// <param name="ackId">The client's cumulative ack — every command up to and including this <see cref="CommandEnvelope.MessageId"/> may be dropped. <see cref="Guid.Empty"/> acks nothing (e.g. the first poll). Ignored when <paramref name="ackMode"/> is false.</param>
    /// <param name="cancellationToken">Cancellation (e.g. the client disconnecting).</param>
    /// <returns>The commands to deliver, in FIFO order (possibly empty).</returns>
    public async Task<IReadOnlyList<CommandEnvelope>> DequeueBatchAsync(int waitMs, bool ackMode, Guid ackId, CancellationToken cancellationToken)
    {
        if (!ackMode)
        {
            return await DequeueLegacyAsync(waitMs, cancellationToken).ConfigureAwait(false);
        }

        // Prune what the client confirmed, then absorb anything newly queued. If the unacked buffer is
        // non-empty (leftover redelivery and/or fresh commands) we return immediately — redelivery must
        // not be held for waitMs.
        lock (_ackGate)
        {
            PruneAcked(ackId);
            DrainChannelInto(_unacked);
            if (_unacked.Count > 0)
            {
                return _unacked.ToArray();
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(waitMs);
        try
        {
            if (await _queue.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
            {
                lock (_ackGate)
                {
                    DrainChannelInto(_unacked);
                    return _unacked.ToArray();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Hold window elapsed (or client disconnected) with nothing queued.
        }

        // Empty hold, or a concurrent poll drained the wake for us: return the current unacked snapshot
        // (still empty unless that concurrent poll added to it).
        lock (_ackGate)
        {
            return _unacked.ToArray();
        }
    }

    // Legacy at-most-once drain — byte-for-byte the pre-ack behavior, so a client that never opts into
    // acking is unaffected by a plugin that supports it.
    private async Task<IReadOnlyList<CommandEnvelope>> DequeueLegacyAsync(int waitMs, CancellationToken cancellationToken)
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

    // Cumulative ack: drop everything from the front of the ordered buffer up to AND INCLUDING the
    // command whose MessageId matches ackId. Guid.Empty (nothing to ack) and an id no longer present
    // (already pruned, or a stale/duplicate ack) both prune nothing. Caller holds _ackGate.
    private void PruneAcked(Guid ackId)
    {
        if (ackId == Guid.Empty || _unacked.Count == 0)
        {
            return;
        }

        var through = _unacked.FindIndex(e => e.MessageId == ackId);
        if (through >= 0)
        {
            _unacked.RemoveRange(0, through + 1);
        }
    }

    // Move everything currently queued into the ordered unacked buffer, then bound it: an ack-capable
    // client that keeps receiving but never acks would otherwise grow it without limit, so keep only the
    // NEWEST QueueCapacity (same DropOldest tradeoff the channel makes). Caller holds _ackGate.
    private void DrainChannelInto(List<CommandEnvelope> buffer)
    {
        while (_queue.Reader.TryRead(out var queued))
        {
            buffer.Add(queued);
        }

        if (buffer.Count > QueueCapacity)
        {
            buffer.RemoveRange(0, buffer.Count - QueueCapacity);
        }
    }

    private bool IsPollFresh() => DateTime.UtcNow.Ticks < Interlocked.Read(ref _freshUntilTicks);
}
