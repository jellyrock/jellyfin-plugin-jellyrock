using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.JellyRock.RemoteControl;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.SyncPlay;
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

    [Fact]
    public async Task Payload_MatchesTheServerSocket_FieldForField()
    {
        // The session socket serializes with JsonDefaults.Options; the long poll must write the same
        // bytes, so the app's one parser never meets a difference on only one transport.
        var controller = new QueueingSessionController();
        var sample = new SocketSample
        {
            ItemId = Guid.Parse("27067fa4-3de4-4a8b-9c1d-0123456789ab"),
            When = new DateTime(2026, 10, 27, 20, 47, 20, 500, DateTimeKind.Utc),
            Missing = null,
            Verb = SampleVerb.Pause
        };

        await controller.SendMessage(SessionMessageType.Playstate, Guid.NewGuid(), sample, CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(1000, ackMode: false, Guid.Empty, CancellationToken.None);
        var data = Data(Assert.Single(batch));

        Assert.Equal(JsonSerializer.Serialize(sample, JsonDefaults.Options), data.GetRawText());
        Assert.Equal("27067fa43de44a8b9c1d0123456789ab", data.GetProperty("ItemId").GetString());
        Assert.False(data.TryGetProperty("Missing", out _));
    }

    [Fact]
    public void HasPolled_IsFalseUntilTheFirstPoll()
    {
        var controller = new QueueingSessionController();
        Assert.False(controller.HasPolled);

        controller.MarkPolled(25000);

        Assert.True(controller.HasPolled);
    }

    [Fact]
    public void ClosedPoll_EndsLiveness_AfterTheGrace()
    {
        var controller = new QueueingSessionController(TimeSpan.Zero);
        controller.MarkPolled(25000);

        controller.MarkPollEnded(closedByClient: true);

        Assert.False(controller.IsSessionActive);
        Assert.True(controller.PollClosedByClient);
        Assert.True(controller.HasPolled);
    }

    [Fact]
    public void ClosedPoll_KeepsLiveness_DuringTheGrace()
    {
        var controller = new QueueingSessionController(TimeSpan.FromMinutes(1));
        controller.MarkPolled(25000);

        controller.MarkPollEnded(closedByClient: true);

        Assert.True(controller.IsSessionActive);
    }

    [Fact]
    public void PollThatEndsNormally_KeepsTheFullWindow()
    {
        var controller = new QueueingSessionController(TimeSpan.Zero);
        controller.MarkPolled(25000);

        controller.MarkPollEnded(closedByClient: false);

        Assert.True(controller.IsSessionActive);
        Assert.False(controller.PollClosedByClient);
    }

    [Fact]
    public void ClosedPoll_WhileAnotherPollIsOpen_KeepsLiveness()
    {
        // An abandoned poll can close after the app's retry has already opened the next one; that close
        // must not cut the live poll's window short.
        var controller = new QueueingSessionController(TimeSpan.Zero);
        controller.MarkPolled(25000);
        controller.MarkPolled(25000);

        controller.MarkPollEnded(closedByClient: true);

        Assert.True(controller.IsSessionActive);
        Assert.False(controller.PollClosedByClient);
    }

    [Fact]
    public void NewPoll_AfterAClosedOne_RestoresLiveness()
    {
        var controller = new QueueingSessionController(TimeSpan.Zero);
        controller.MarkPolled(25000);
        controller.MarkPollEnded(closedByClient: true);

        controller.MarkPolled(25000);

        Assert.True(controller.IsSessionActive);
        Assert.False(controller.PollClosedByClient);
    }

    [Fact]
    public async Task SyncPlayMembership_FollowsTheServersGroupFrames()
    {
        var controller = new QueueingSessionController();
        var groupId = Guid.NewGuid();
        Assert.False(controller.IsInSyncPlayGroup);

        await SendGroupUpdate(controller, new SyncPlayGroupJoinedUpdate(groupId, new GroupInfoDto(groupId, "Movie night", GroupStateType.Idle, new[] { "ann" }, DateTime.UtcNow)));
        Assert.True(controller.IsInSyncPlayGroup);

        // A frame that is not about this session's membership leaves it as it is.
        await SendGroupUpdate(controller, new SyncPlayUserJoinedUpdate(groupId, "bob"));
        Assert.True(controller.IsInSyncPlayGroup);

        await SendGroupUpdate(controller, new SyncPlayGroupLeftUpdate(groupId, string.Empty));
        Assert.False(controller.IsInSyncPlayGroup);
    }

    [Fact]
    public async Task SyncPlayMembership_NotInGroup_ClearsIt()
    {
        var controller = new QueueingSessionController();
        var groupId = Guid.NewGuid();
        await SendGroupUpdate(controller, new SyncPlayGroupJoinedUpdate(groupId, new GroupInfoDto(groupId, "Movie night", GroupStateType.Idle, new[] { "ann" }, DateTime.UtcNow)));

        await SendGroupUpdate(controller, new SyncPlayNotInGroupUpdate(Guid.Empty, string.Empty));

        Assert.False(controller.IsInSyncPlayGroup);
    }

    [Fact]
    public async Task ForgetSyncPlayGroup_ClearsMembership()
    {
        var controller = new QueueingSessionController();
        var groupId = Guid.NewGuid();
        await SendGroupUpdate(controller, new SyncPlayGroupJoinedUpdate(groupId, new GroupInfoDto(groupId, "Movie night", GroupStateType.Idle, new[] { "ann" }, DateTime.UtcNow)));

        controller.ForgetSyncPlayGroup();

        Assert.False(controller.IsInSyncPlayGroup);
    }

    // The server sends group updates typed as the abstract GroupUpdate<T>, so the tests do too.
    private static Task SendGroupUpdate<T>(QueueingSessionController controller, GroupUpdate<T> update) =>
        controller.SendMessage(SessionMessageType.SyncPlayGroupUpdate, Guid.NewGuid(), update, CancellationToken.None);

    private static JsonElement Data(CommandEnvelope envelope) => (JsonElement)envelope.Data!;

    private sealed class SocketSample
    {
        public Guid ItemId { get; set; }

        public DateTime When { get; set; }

        public string? Missing { get; set; }

        public SampleVerb Verb { get; set; }
    }

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
