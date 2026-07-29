using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowAccountabilityObserverTests
{
    [Theory]
    [InlineData("workflow.started", "indeterminate")]
    [InlineData("workflow.suspended", "indeterminate")]
    [InlineData("workflow.resumed", "indeterminate")]
    [InlineData("workflow.completed", "succeeded")]
    [InlineData("workflow.failed", "failed")]
    public async Task MapsLifecycleTargetAndOutcome(string eventType, string expectedStatus)
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);
        await observer.ObserveAsync(new WorkflowLifecycleEvent
        {
            EventType = eventType,
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 3,
            OccurredAt = DateTimeOffset.UnixEpoch,
            WorkflowRunOperationId = "run-1"
        });

        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Target.Kind.Should().Be("workflow-instance");
        recorder.Envelope.Target.Id.Should().Be("instance-1");
        recorder.Envelope.Outcome.Status.Should().Be(expectedStatus);
        recorder.Envelope.Descriptors.Items.Single().Version.Should().Be(3);
        recorder.Envelope.Actor.Kind.Should().Be("workflow");
    }

    [Fact]
    public async Task HumanTaskCompletionEventIsDirectCauseAndReference()
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);
        await observer.ObserveAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.resumed",
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 1,
            CausationId = "completion-event-1",
            HumanTaskCompletionEventId = "completion-event-1"
        });

        recorder.Envelope!.CausationId.Should().Be("completion-event-1");
        recorder.Envelope.Runtime.References.Should().Contain(x => x.Kind == "event-instance");
    }

    private sealed class CaptureRecorder : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(new AuditRecordResult { AuditId = envelope.AuditId, Status = AuditRecordStatus.Recorded, ProcessedAt = DateTimeOffset.UtcNow });
        }
    }

    private sealed class FixedIdentity : IAuditIdentityGenerator
    {
        public string CreateAuditId() => "audit-1";
        public string CreateOperationId() => "operation-1";
    }
}
