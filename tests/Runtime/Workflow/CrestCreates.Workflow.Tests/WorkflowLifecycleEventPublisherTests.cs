using System.Collections.Concurrent;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowLifecycleEventPublisherTests
{
    [Fact]
    public async Task CommittedTransitionStillNotifiesWhenBusinessTokenIsCancelled()
    {
        var observer = new RecordingObserver("observer");
        var publisher = WorkflowTestAccountability.CreatePublisher([observer]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await publisher.PublishAsync(CreateEvent(), cancellation.Token);

        observer.Calls.Should().Be(1);
    }

    [Fact]
    public async Task OneObserverFailureDoesNotSuppressLaterObserver()
    {
        var started = new ConcurrentBag<string>();
        var publisher = WorkflowTestAccountability.CreatePublisher([
            new RecordingObserver("a", started, throwSynchronously: true),
            new RecordingObserver("b", started)
        ]);

        await publisher.PublishAsync(CreateEvent(), CancellationToken.None);

        started.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task IncompleteAsyncObserverIsBoundedByNotificationTimeout()
    {
        var started = new ConcurrentBag<string>();
        var publisher = WorkflowTestAccountability.CreatePublisher([
            new RecordingObserver("a", started, neverComplete: true),
            new RecordingObserver("b", started)
        ], TimeSpan.FromMilliseconds(40));

        var action = () => publisher.PublishAsync(CreateEvent(), CancellationToken.None);

        await action.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1));
        started.Should().BeEquivalentTo("a", "b");
    }

    private static WorkflowLifecycleEvent CreateEvent()
        => new()
        {
            EventId = "event-1",
            AuditId = "audit-1",
            EventType = "workflow.completed",
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 1,
            OccurredAt = DateTimeOffset.UnixEpoch,
            WorkflowRunOperationId = "run-1"
        };

    private sealed class RecordingObserver : IWorkflowLifecycleObserver
    {
        private readonly ConcurrentBag<string>? _started;
        private readonly bool _throwSynchronously;
        private readonly bool _neverComplete;

        public RecordingObserver(
            string id,
            ConcurrentBag<string>? started = null,
            bool throwSynchronously = false,
            bool neverComplete = false)
        {
            Id = id;
            _started = started;
            _throwSynchronously = throwSynchronously;
            _neverComplete = neverComplete;
        }

        public string Id { get; }
        public int Calls { get; private set; }

        public ValueTask ObserveAsync(
            WorkflowLifecycleEvent lifecycleEvent,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            _started?.Add(Id);
            if (_throwSynchronously) throw new InvalidOperationException("observer failed");
            return _neverComplete
                ? new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken))
                : ValueTask.CompletedTask;
        }
    }
}
