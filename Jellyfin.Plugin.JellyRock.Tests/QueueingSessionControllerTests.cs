using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.JellyRock.Tests;

/// <summary>
/// Unit tests for <see cref="QueueingSessionController"/> — the queue + poll-liveness logic that
/// carries the highest-risk behavior (the enum-as-string wire fix, the single-reader concurrency
/// fix, and the derived liveness window).
/// </summary>
public class QueueingSessionControllerTests
{
    private enum SampleVerb
    {
        Pause,
        Play
    }

    [Fact]
    public void IsInactive_BeforeAnyPoll()
    {
        var controller = new QueueingSessionController();

        Assert.False(controller.IsSessionActive);
        Assert.False(controller.SupportsMediaControl);
    }

    [Fact]
    public void IsActive_ImmediatelyAfterPoll()
    {
        var controller = new QueueingSessionController();

        controller.MarkPolled(25000);

        Assert.True(controller.IsSessionActive);
        Assert.True(controller.SupportsMediaControl);
    }

    [Fact]
    public async Task GoesStale_AfterTwiceTheWaitElapses()
    {
        var controller = new QueueingSessionController();

        // grace = 2 * waitMs. With waitMs=15ms the window is 30ms; wait well past it.
        controller.MarkPolled(15);
        Assert.True(controller.SupportsMediaControl);

        await Task.Delay(120);

        Assert.False(controller.SupportsMediaControl);
        Assert.False(controller.IsSessionActive);
    }

    [Fact]
    public async Task EnqueuedCommand_IsReturnedWithItsMessageType()
    {
        var controller = new QueueingSessionController();
        var id = Guid.NewGuid();

        await controller.SendMessage(SessionMessageType.Playstate, id, new { Command = SampleVerb.Pause }, CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(1000, ackMode: false, Guid.Empty, CancellationToken.None);

        var envelope = Assert.Single(batch);
        Assert.Equal("Playstate", envelope.MessageType);
        Assert.Equal(id, envelope.MessageId);
    }

    [Fact]
    public async Task EnumFields_SerializeAsStrings_NotIntegers()
    {
        // Guards the shipped bug: System.Text.Json's default emits enums as ints, which the client
        // can't match, silently no-op'ing every enum-carrying command.
        var controller = new QueueingSessionController();

        await controller.SendMessage(SessionMessageType.Playstate, Guid.NewGuid(), new { Command = SampleVerb.Pause }, CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(1000, ackMode: false, Guid.Empty, CancellationToken.None);
        var command = Data(Assert.Single(batch)).GetProperty("Command");

        Assert.Equal("Pause", command.GetString());
    }

    [Theory]
    [InlineData(SessionMessageType.KeepAlive)]
    [InlineData(SessionMessageType.ForceKeepAlive)]
    public async Task KeepAliveFrames_AreNeverEnqueued(SessionMessageType type)
    {
        var controller = new QueueingSessionController();

        await controller.SendMessage(type, Guid.NewGuid(), "60", CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(50, ackMode: false, Guid.Empty, CancellationToken.None);
        Assert.Empty(batch);
    }

    [Fact]
    public async Task Batch_PreservesFifoOrder()
    {
        var controller = new QueueingSessionController();

        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoHome" }, CancellationToken.None);
        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoToSearch" }, CancellationToken.None);
        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoToSettings" }, CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(1000, ackMode: false, Guid.Empty, CancellationToken.None);

        Assert.Equal(
            new[] { "GoHome", "GoToSearch", "GoToSettings" },
            batch.Select(e => Data(e).GetProperty("Name").GetString()));
    }

    private static JsonElement Data(CommandEnvelope envelope) => (JsonElement)envelope.Data!;

    [Fact]
    public async Task EmptyPoll_ReturnsEmptyAfterHold()
    {
        var controller = new QueueingSessionController();

        var batch = await controller.DequeueBatchAsync(50, ackMode: false, Guid.Empty, CancellationToken.None);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task ConcurrentDequeue_DoesNotThrow_AndDeliversTheCommand()
    {
        // Guards the single-reader fix: two overlapping polls for one session must not violate a
        // channel invariant. Both wait; one write must be delivered by exactly one of them.
        var controller = new QueueingSessionController();

        var pollA = controller.DequeueBatchAsync(2000, ackMode: false, Guid.Empty, CancellationToken.None);
        var pollB = controller.DequeueBatchAsync(2000, ackMode: false, Guid.Empty, CancellationToken.None);

        await Task.Delay(30); // let both park in WaitToReadAsync
        await controller.SendMessage(SessionMessageType.Play, Guid.NewGuid(), new { PlayCommand = "PlayNow" }, CancellationToken.None);

        var results = await Task.WhenAll(pollA, pollB);

        var delivered = results.SelectMany(r => r).ToList();
        Assert.Single(delivered);
        Assert.Equal("Play", delivered[0].MessageType);
    }

    // --- Opt-in at-least-once (ackMode) ---------------------------------------------------------

    private static async Task<Guid> Enqueue(QueueingSessionController controller, string name)
    {
        var id = Guid.NewGuid();
        await controller.SendMessage(SessionMessageType.GeneralCommand, id, new { Name = name }, CancellationToken.None);
        return id;
    }

    private static async Task<IReadOnlyList<CommandEnvelope>> AckPoll(QueueingSessionController controller, Guid ackId, int waitMs = 1000)
        => await controller.DequeueBatchAsync(waitMs, ackMode: true, ackId, CancellationToken.None);

    [Fact]
    public async Task Legacy_DropsBatchAfterDelivery_AtMostOnce()
    {
        // The baseline this feature moves away from: a legacy (non-ack) poll removes the batch, so a
        // second poll — the client's retry after a lost response — gets nothing.
        var controller = new QueueingSessionController();
        await Enqueue(controller, "GoHome");

        var first = await controller.DequeueBatchAsync(1000, ackMode: false, Guid.Empty, CancellationToken.None);
        var second = await controller.DequeueBatchAsync(50, ackMode: false, Guid.Empty, CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public async Task AckMode_RedeliversUnackedBatch_WhenNextPollDoesNotAck()
    {
        // The fix: a poll whose response was lost (client never acked) redelivers on the next poll.
        var controller = new QueueingSessionController();
        var id = await Enqueue(controller, "GoHome");

        var first = await AckPoll(controller, Guid.Empty);
        var redelivered = await AckPoll(controller, Guid.Empty); // no ack -> same batch again

        Assert.Equal(id, Assert.Single(first).MessageId);
        Assert.Equal(id, Assert.Single(redelivered).MessageId);
    }

    [Fact]
    public async Task AckMode_PrunesAckedBatch_ThenDeliversOnlyNew()
    {
        // Happy path: the client acks what it got, so it is not redelivered; only new commands follow.
        var controller = new QueueingSessionController();
        var a = await Enqueue(controller, "GoHome");

        var first = await AckPoll(controller, Guid.Empty);
        Assert.Equal(a, Assert.Single(first).MessageId);

        var b = await Enqueue(controller, "GoToSearch");
        var second = await AckPoll(controller, a); // ack A

        Assert.Equal(b, Assert.Single(second).MessageId);
    }

    [Fact]
    public async Task AckMode_CumulativeAck_PrunesThrough_AndPreservesOrder()
    {
        var controller = new QueueingSessionController();
        var a = await Enqueue(controller, "GoHome");
        var b = await Enqueue(controller, "GoToSearch");
        var c = await Enqueue(controller, "GoToSettings");

        var first = await AckPoll(controller, Guid.Empty);
        Assert.Equal(new[] { a, b, c }, first.Select(e => e.MessageId));

        // Ack the middle id -> A and B drop, C survives and redelivers.
        var second = await AckPoll(controller, b);
        Assert.Equal(c, Assert.Single(second).MessageId);
    }

    [Fact]
    public async Task AckMode_UnknownOrStaleAckId_PrunesNothing()
    {
        var controller = new QueueingSessionController();
        var id = await Enqueue(controller, "GoHome");

        await AckPoll(controller, Guid.Empty);
        var redelivered = await AckPoll(controller, Guid.NewGuid()); // an id we never sent

        Assert.Equal(id, Assert.Single(redelivered).MessageId);
    }

    [Fact]
    public async Task AckMode_AcksEverything_ThenHoldsEmpty()
    {
        var controller = new QueueingSessionController();
        var id = await Enqueue(controller, "GoHome");

        await AckPoll(controller, Guid.Empty);
        var empty = await AckPoll(controller, id, waitMs: 50); // acked -> nothing left, short hold

        Assert.Empty(empty);
    }

    [Fact]
    public async Task AckMode_BoundsUnackedBuffer_DroppingOldestAcrossDrains()
    {
        // An ack-capable client that keeps receiving but never acks must not grow the buffer without
        // bound. Two un-acked drains of 150 accumulate to 300, then keep only the newest 256.
        var controller = new QueueingSessionController();

        var firstIds = new List<Guid>();
        for (var i = 0; i < 150; i++)
        {
            firstIds.Add(await Enqueue(controller, "c" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        await AckPoll(controller, Guid.Empty); // received, not acked

        var secondIds = new List<Guid>();
        for (var i = 0; i < 150; i++)
        {
            secondIds.Add(await Enqueue(controller, "d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var batch = await AckPoll(controller, Guid.Empty); // still no ack -> drain + accumulate + cap

        Assert.Equal(256, batch.Count);
        Assert.Equal(secondIds[^1], batch[^1].MessageId);      // newest is kept
        Assert.DoesNotContain(firstIds[0], batch.Select(e => e.MessageId)); // oldest is dropped
    }
}
