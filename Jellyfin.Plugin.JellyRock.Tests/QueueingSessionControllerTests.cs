using System;
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

        var batch = await controller.DequeueBatchAsync(1000, CancellationToken.None);

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

        var batch = await controller.DequeueBatchAsync(1000, CancellationToken.None);
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

        var batch = await controller.DequeueBatchAsync(50, CancellationToken.None);
        Assert.Empty(batch);
    }

    [Fact]
    public async Task Batch_PreservesFifoOrder()
    {
        var controller = new QueueingSessionController();

        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoHome" }, CancellationToken.None);
        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoToSearch" }, CancellationToken.None);
        await controller.SendMessage(SessionMessageType.GeneralCommand, Guid.NewGuid(), new { Name = "GoToSettings" }, CancellationToken.None);

        var batch = await controller.DequeueBatchAsync(1000, CancellationToken.None);

        Assert.Equal(
            new[] { "GoHome", "GoToSearch", "GoToSettings" },
            batch.Select(e => Data(e).GetProperty("Name").GetString()));
    }

    private static JsonElement Data(CommandEnvelope envelope) => (JsonElement)envelope.Data!;

    [Fact]
    public async Task EmptyPoll_ReturnsEmptyAfterHold()
    {
        var controller = new QueueingSessionController();

        var batch = await controller.DequeueBatchAsync(50, CancellationToken.None);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task ConcurrentDequeue_DoesNotThrow_AndDeliversTheCommand()
    {
        // Guards the single-reader fix: two overlapping polls for one session must not violate a
        // channel invariant. Both wait; one write must be delivered by exactly one of them.
        var controller = new QueueingSessionController();

        var pollA = controller.DequeueBatchAsync(2000, CancellationToken.None);
        var pollB = controller.DequeueBatchAsync(2000, CancellationToken.None);

        await Task.Delay(30); // let both park in WaitToReadAsync
        await controller.SendMessage(SessionMessageType.Play, Guid.NewGuid(), new { PlayCommand = "PlayNow" }, CancellationToken.None);

        var results = await Task.WhenAll(pollA, pollB);

        var delivered = results.SelectMany(r => r).ToList();
        Assert.Single(delivered);
        Assert.Equal("Play", delivered[0].MessageType);
    }
}
