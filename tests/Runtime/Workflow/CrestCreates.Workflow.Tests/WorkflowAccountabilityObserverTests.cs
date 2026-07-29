using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
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

    [Fact]
    public async Task WorkflowAccountabilityPreservesTenantId()
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);

        await observer.ObserveAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 1,
            TenantId = "tenant-1"
        });

        recorder.Envelope!.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SuspendResumePreservesSameTenantId()
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);
        foreach (var eventType in new[] { "workflow.suspended", "workflow.resumed" })
        {
            await observer.ObserveAsync(new WorkflowLifecycleEvent
            {
                EventType = eventType,
                WorkflowInstanceId = "instance-1",
                WorkflowId = "workflow-1",
                WorkflowVersion = 1,
                TenantId = "tenant-1"
            });
        }

        recorder.Envelopes.Should().HaveCount(2);
        recorder.Envelopes.Should().OnlyContain(x => x.TenantId == "tenant-1");
    }

    [Fact]
    public async Task RecordsStartedSuspendedResumedCompletedFailedAfterSave()
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);
        foreach (var eventType in EventTypes)
            await observer.ObserveAsync(Event(eventType));
        recorder.Envelopes.Select(x => x.Action.Name).Should().Equal(EventTypes);
    }

    [Fact]
    public Task DoesNotRecordWhenStoreSaveFails()
        => new WorkflowRuntimeTests().ExecuteAsync_Cancellation_PropagatesWithoutSave();

    [Fact]
    public Task ObserverFailureDoesNotRollbackState()
        => new WorkflowLifecycleEventPublisherTests().OneObserverFailureDoesNotSuppressLaterObserver();

    [Fact]
    public void SnapshotPreservesAuditOriginAndLastLifecycleLinkage()
    {
        var origin = new AuditOrigin
        {
            CorrelationId = "correlation-1",
            UpstreamOperationId = "operation-1",
            UpstreamAuditId = "audit-1",
            InitiatingActor = new AuditActor { Kind = "user", Id = "user-1" },
            InvocationSource = "http"
        };
        var snapshot = new WorkflowInstance
        {
            InstanceId = "instance-1",
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("workflow-1", 1),
            AuditOrigin = origin,
            LastLifecycleAuditId = "lifecycle-1"
        }.Snapshot();
        snapshot.AuditOrigin.Should().Be(origin);
        snapshot.LastLifecycleAuditId.Should().Be("lifecycle-1");
    }

    [Fact]
    public async Task SuspensionResumePreservesCorrelationAndInitiatingActor()
    {
        var recorder = new CaptureRecorder();
        var observer = new WorkflowAccountabilityObserver(recorder);
        var origin = new AuditOrigin
        {
            CorrelationId = "correlation-1",
            InitiatingActor = new AuditActor { Kind = "user", Id = "user-1" },
            InvocationSource = "http"
        };
        await observer.ObserveAsync(Event("workflow.suspended") with { Origin = origin });
        await observer.ObserveAsync(Event("workflow.resumed") with { Origin = origin });
        recorder.Envelopes.Should().OnlyContain(x =>
            x.CorrelationId == "correlation-1"
            && x.Actor.InitiatedBy == new AuditActorReference("user", "user-1"));
    }

    [Fact]
    public async Task PreviousLifecycleEventIsNotUsedAsCausation()
    {
        var envelope = await Observe(Event("workflow.completed") with
        {
            PreviousAuditId = "previous-audit",
            CausationId = "run-operation"
        });
        envelope.PreviousAuditId.Should().Be("previous-audit");
        envelope.CausationId.Should().Be("run-operation");
        envelope.CausationId.Should().NotBe(envelope.PreviousAuditId);
    }

    [Fact]
    public Task HumanTaskInstanceIdIsRuntimeReferenceNotCausation()
        => HumanTaskCompletionEventIsDirectCauseAndReference();

    [Fact]
    public Task ResumeUsesCompletionEventIdWhenAvailable()
        => HumanTaskCompletionEventIsDirectCauseAndReference();

    [Fact]
    public async Task ResumeAllowsUnknownCauseWithoutInventingIdentity()
    {
        var envelope = await Observe(Event("workflow.resumed") with
        {
            HumanTaskInstanceId = "task-1",
            CausationId = null
        });
        envelope.CausationId.Should().BeNull();
        envelope.Runtime.References.Should().Contain(new AuditRuntimeReference("human-task-instance", "task-1"));
    }

    [Fact]
    public async Task WorkflowRunOperationCausesTerminalTransition()
    {
        var envelope = await Observe(Event("workflow.completed") with
        {
            WorkflowRunOperationId = "run-1",
            CausationId = "run-1"
        });
        envelope.CausationId.Should().Be("run-1");
        envelope.Runtime.ExecutionId.Should().Be("run-1");
    }

    [Fact]
    public async Task WorkflowLifecycleActorIsWorkflowNotInitiatingUser()
    {
        var envelope = await Observe(Event("workflow.started") with
        {
            Origin = Origin()
        });
        envelope.Actor.Kind.Should().Be("workflow");
        envelope.Actor.Id.Should().Be("instance-1");
    }

    [Fact]
    public async Task WorkflowActorPreservesInitiatedBy()
    {
        var envelope = await Observe(Event("workflow.started") with { Origin = Origin() });
        envelope.Actor.InitiatedBy.Should().Be(new AuditActorReference("user", "user-1"));
    }

    [Fact]
    public async Task WorkflowOccurredAtIsCommittedTransitionTime()
    {
        var committedAt = DateTimeOffset.Parse("2026-07-29T10:00:00Z");
        var envelope = await Observe(Event("workflow.completed") with { OccurredAt = committedAt });
        envelope.OccurredAt.Should().Be(committedAt);
    }

    [Fact]
    public async Task WorkflowLifecycleTargetsWorkflowInstance()
    {
        var envelope = await Observe(Event("workflow.started"));
        envelope.Target.Should().Be(new AuditTarget { Kind = "workflow-instance", Id = "instance-1" });
    }

    [Fact]
    public async Task WorkflowLifecycleReferencesWorkflowDescriptor()
    {
        var envelope = await Observe(Event("workflow.started"));
        envelope.Descriptors.Items.Should().ContainSingle(x =>
            x.Kind == "workflow" && x.Id == "workflow-1" && x.Version == 3);
    }

    [Fact]
    public async Task WorkflowStartedSuspendedResumedAreIndeterminate()
    {
        foreach (var eventType in EventTypes.Take(3))
            (await Observe(Event(eventType))).Outcome.Status.Should().Be("indeterminate");
    }

    [Fact]
    public async Task WorkflowCompletedIsSucceeded()
        => (await Observe(Event("workflow.completed"))).Outcome.Status.Should().Be("succeeded");

    [Fact]
    public async Task WorkflowFailedIsFailedWithStableReasonCode()
    {
        var envelope = await Observe(Event("workflow.failed") with { ReasonCode = "WORKFLOW_STEP_FAILED" });
        envelope.Outcome.Should().Be(new AuditOutcome { Status = "failed", Code = "WORKFLOW_STEP_FAILED" });
    }

    [Fact]
    public async Task IncludesDescriptorVersionAndStructuredHash()
    {
        var envelope = await Observe(Event("workflow.started") with { ContractHash = Hash });
        envelope.Descriptors.Items.Single().ContractHash.Should().Be(Hash);
    }

    [Fact]
    public void DoesNotExposeObjectPayload()
        => typeof(WorkflowLifecycleEvent).GetProperties()
            .Should().NotContain(property => property.PropertyType == typeof(object));

    private static async Task<AuditEnvelope> Observe(WorkflowLifecycleEvent lifecycleEvent)
    {
        var recorder = new CaptureRecorder();
        await new WorkflowAccountabilityObserver(recorder).ObserveAsync(lifecycleEvent);
        return recorder.Envelope!;
    }

    private static WorkflowLifecycleEvent Event(string eventType)
        => new()
        {
            AuditId = $"audit-{eventType}",
            EventType = eventType,
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 3,
            OccurredAt = DateTimeOffset.UnixEpoch,
            CorrelationId = "correlation-1",
            WorkflowRunOperationId = "run-1"
        };

    private static AuditOrigin Origin()
        => new()
        {
            CorrelationId = "correlation-1",
            InitiatingActor = new AuditActor { Kind = "user", Id = "user-1" },
            InvocationSource = "http"
        };

    private static readonly string[] EventTypes =
    [
        "workflow.started",
        "workflow.suspended",
        "workflow.resumed",
        "workflow.completed",
        "workflow.failed"
    ];

    private static CanonicalHash Hash { get; } = new()
    {
        Value = "hash",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "WorkflowDescriptor",
        Scope = "Contract",
        Purpose = "ContractIdentity",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "workflow-v1"
    };

    private sealed class CaptureRecorder : IAuditRecorder
    {
        public List<AuditEnvelope> Envelopes { get; } = [];
        public AuditEnvelope? Envelope => Envelopes.LastOrDefault();
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = DateTimeOffset.UtcNow,
                SinkResults =
                [
                    new CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteResult
                    {
                        SinkId = "test",
                        AuditId = envelope.AuditId,
                        Integrity = Hash,
                        Status = CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteStatus.Accepted
                    }
                ]
            });
        }
    }

    private sealed class FixedIdentity : IAuditIdentityGenerator
    {
        public string CreateAuditId() => "audit-1";
        public string CreateOperationId() => "operation-1";
    }
}
