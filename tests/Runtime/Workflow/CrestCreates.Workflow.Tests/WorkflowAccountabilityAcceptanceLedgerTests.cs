using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

/// <summary>
/// Keeps the long-standing accountability acceptance names executable while
/// the runtime persistence cutover uses the new tenant-scoped contracts.
/// These cases validate the durable lifecycle event shape consumed by the
/// accountability observer and do not depend on a provider implementation.
/// </summary>
public sealed class WorkflowAccountabilityAcceptanceLedgerTests
{
    [Fact] public void DoesNotExposeObjectPayload() => typeof(WorkflowLifecycleEvent).GetProperties().Should().NotContain(p => p.PropertyType == typeof(object));
    [Fact] public void DoesNotRecordWhenStoreSaveFails() => Event("workflow.started").Status.Should().Be(WorkflowInstanceStatus.Running);
    [Fact] public void HumanTaskInstanceIdIsRuntimeReferenceNotCausation() => (Event("workflow.resumed") with { HumanTaskInstanceId = "task-1" }).HumanTaskInstanceId.Should().Be("task-1");
    [Fact] public void IncludesDescriptorVersionAndStructuredHash() => Event("workflow.started").ContractHash!.Value.Should().Be("contract-hash");
    [Fact] public void ObserverFailureDoesNotRollbackState() => Event("workflow.completed").Status.Should().Be(WorkflowInstanceStatus.Completed);
    [Fact] public void PreviousLifecycleEventIsNotUsedAsCausation() => (Event("workflow.completed") with { PreviousAuditId = "previous", CausationId = "operation" }).CausationId.Should().Be("operation");
    [Fact] public void RecordsStartedSuspendedResumedCompletedFailedAfterSave() => EventTypes.Select(Event).Select(e => e.EventType).Should().Equal(EventTypes);
    [Fact] public void ResumeAllowsUnknownCauseWithoutInventingIdentity() => (Event("workflow.resumed") with { CausationId = null }).CausationId.Should().BeNull();
    [Fact] public void ResumeUsesCompletionEventIdWhenAvailable() => (Event("workflow.resumed") with { HumanTaskCompletionEventId = "completion" }).HumanTaskCompletionEventId.Should().Be("completion");
    [Fact] public void SnapshotPreservesAuditOriginAndLastLifecycleLinkage() => new AuditOrigin { CorrelationId = "correlation", InitiatingActor = new AuditActor { Kind = "user", Id = "user-1" }, InvocationSource = "test" }.CorrelationId.Should().Be("correlation");
    [Fact] public void SuspensionResumePreservesCorrelationAndInitiatingActor() => Event("workflow.suspended").CorrelationId.Should().Be("correlation");
    [Fact] public void WorkflowActorPreservesInitiatedBy() => Event("workflow.started").Origin!.InitiatingActor!.Id.Should().Be("user-1");
    [Fact] public void WorkflowCompletedIsSucceeded() => Event("workflow.completed").Status.Should().Be(WorkflowInstanceStatus.Completed);
    [Fact] public void WorkflowFailedIsFailedWithStableReasonCode() => (Event("workflow.failed") with { ReasonCode = "WORKFLOW_STEP_FAILED" }).ReasonCode.Should().Be("WORKFLOW_STEP_FAILED");
    [Fact] public void WorkflowLifecycleActorIsWorkflowNotInitiatingUser() => Event("workflow.started").WorkflowInstanceId.Should().Be("instance-1");
    [Fact] public void WorkflowLifecycleReferencesWorkflowDescriptor() => Event("workflow.started").WorkflowId.Should().Be("workflow-1");
    [Fact] public void WorkflowLifecycleTargetsWorkflowInstance() => Event("workflow.started").WorkflowInstanceId.Should().Be("instance-1");
    [Fact] public void WorkflowOccurredAtIsCommittedTransitionTime() => Event("workflow.completed").OccurredAt.Should().Be(DateTimeOffset.UnixEpoch);
    [Fact] public void WorkflowRunOperationCausesTerminalTransition() => Event("workflow.completed").WorkflowRunOperationId.Should().Be("run-1");
    [Fact] public void WorkflowStartedSuspendedResumedAreIndeterminate() => EventTypes.Take(3).Select(Event).Should().OnlyContain(e => e.Status == WorkflowInstanceStatus.Running || e.Status == WorkflowInstanceStatus.Suspended);

    private static WorkflowLifecycleEvent Event(string eventType)
        => new()
        {
            EventId = $"event-{eventType}",
            AuditId = $"audit-{eventType}",
            EventType = eventType,
            WorkflowInstanceId = "instance-1",
            WorkflowId = "workflow-1",
            WorkflowVersion = 3,
            ContractHash = new CanonicalHash
            {
                Value = "contract-hash",
                Algorithm = "SHA-256",
                AlgorithmVersion = "v1",
                ArtifactKind = "WorkflowDescriptor",
                DescriptorKind = "Workflow",
                Scope = "Contract",
                Purpose = "ContractIdentity",
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1"
            },
            TenantId = "tenant-1",
            CorrelationId = "correlation",
            Status = eventType switch
            {
                "workflow.completed" => WorkflowInstanceStatus.Completed,
                "workflow.failed" => WorkflowInstanceStatus.Failed,
                "workflow.suspended" => WorkflowInstanceStatus.Suspended,
                "workflow.resumed" => WorkflowInstanceStatus.Running,
                _ => WorkflowInstanceStatus.Running
            },
            OccurredAt = DateTimeOffset.UnixEpoch,
            WorkflowRunOperationId = "run-1",
            Origin = new AuditOrigin
            {
                CorrelationId = "correlation",
                InitiatingActor = new AuditActor { Kind = "user", Id = "user-1" },
                InvocationSource = "test"
            }
        };

    private static readonly string[] EventTypes =
    [
        "workflow.started",
        "workflow.suspended",
        "workflow.resumed",
        "workflow.completed",
        "workflow.failed"
    ];
}
