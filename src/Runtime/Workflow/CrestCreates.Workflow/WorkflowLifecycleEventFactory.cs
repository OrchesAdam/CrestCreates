using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowLifecycleEventFactory
{
    private readonly IAuditIdentityGenerator _identity;
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly TimeProvider _timeProvider;

    public WorkflowLifecycleEventFactory(
        IAuditIdentityGenerator identity,
        IDescriptorStableHashBuilder hashBuilder,
        TimeProvider? timeProvider = null)
    {
        _identity = identity;
        _hashBuilder = hashBuilder;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string CreateRunOperationId() => _identity.CreateOperationId();

    public WorkflowLifecycleIdentity AllocateLifecycleIdentity()
        => new(_identity.CreateOperationId(), _identity.CreateAuditId());

    public WorkflowLifecycleEvent Create(
        string eventType,
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        WorkflowLifecycleIdentity identity,
        string workflowRunOperationId,
        WorkflowInstanceStatus? fromStatus,
        string? causationId,
        string? parentAuditId,
        string? previousAuditId,
        string? reasonCode = null,
        string? stepId = null,
        string? humanTaskInstanceId = null,
        string? humanTaskCompletionEventId = null)
    {
        var stableEventType = eventType switch
        {
            "workflow.started" => "workflow.started",
            "workflow.suspended" => "workflow.suspended",
            "workflow.resumed" => "workflow.resumed",
            "workflow.completed" => "workflow.completed",
            "workflow.failed" => "workflow.failed",
            _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported Workflow lifecycle event.")
        };

        return new WorkflowLifecycleEvent
        {
            EventId = identity.EventId,
            AuditId = identity.AuditId,
            EventType = stableEventType,
            OccurredAt = _timeProvider.GetUtcNow(),
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            WorkflowVersion = descriptor.Version,
            ContractHash = _hashBuilder.Build(descriptor).ContractHash,
            TenantId = instance.TenantId,
            CorrelationId = instance.AuditOrigin?.CorrelationId,
            Status = instance.Status,
            FromStatus = fromStatus,
            ToStatus = instance.Status,
            CausationId = causationId,
            ParentAuditId = parentAuditId,
            PreviousAuditId = previousAuditId,
            WorkflowRunOperationId = workflowRunOperationId,
            StepId = stepId,
            HumanTaskInstanceId = humanTaskInstanceId,
            ReasonCode = stableEventType == "workflow.failed" && string.IsNullOrWhiteSpace(reasonCode)
                ? "WORKFLOW_FAILED"
                : reasonCode,
            Origin = instance.AuditOrigin,
            HumanTaskCompletionEventId = humanTaskCompletionEventId
        };
    }
}

internal readonly record struct WorkflowLifecycleIdentity(string EventId, string AuditId);
