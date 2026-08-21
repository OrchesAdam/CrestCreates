using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow.Accountability;

internal static class WorkflowAccountabilityEnvelopeFactory
{
    internal static AuditEnvelope Create(WorkflowLifecycleEvent lifecycleEvent)
    {
        var references = new List<AuditRuntimeReference> { new("workflow-instance", lifecycleEvent.WorkflowInstanceId) };
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.WorkflowRunOperationId)) references.Add(new("workflow-run-operation", lifecycleEvent.WorkflowRunOperationId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.StepId)) references.Add(new("workflow-step", lifecycleEvent.StepId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.HumanTaskInstanceId)) references.Add(new("human-task-instance", lifecycleEvent.HumanTaskInstanceId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.HumanTaskCompletionEventId)) references.Add(new("event-instance", lifecycleEvent.HumanTaskCompletionEventId));
        var initiatingActor = lifecycleEvent.Origin?.InitiatingActor;
        var eventType = lifecycleEvent.EventType switch
        {
            "workflow.started" or "workflow.suspended" or "workflow.resumed" or "workflow.completed" or "workflow.failed" => lifecycleEvent.EventType,
            _ => throw new InvalidOperationException($"Unsupported workflow lifecycle event '{lifecycleEvent.EventType}'.")
        };
        return new AuditEnvelope
        {
            AuditId = lifecycleEvent.AuditId,
            OccurredAt = lifecycleEvent.OccurredAt,
            TenantId = lifecycleEvent.TenantId,
            CorrelationId = lifecycleEvent.CorrelationId ?? lifecycleEvent.Origin?.CorrelationId ?? string.Empty,
            CausationId = lifecycleEvent.CausationId,
            ParentAuditId = lifecycleEvent.ParentAuditId,
            PreviousAuditId = lifecycleEvent.PreviousAuditId,
            Actor = new AuditActor { Kind = "workflow", Id = lifecycleEvent.WorkflowInstanceId, InitiatedBy = initiatingActor is null ? null : new AuditActorReference(initiatingActor.Kind, initiatingActor.Id) },
            Action = new AuditAction { Kind = "workflow.lifecycle", Name = eventType },
            Target = new AuditTarget { Kind = "workflow-instance", Id = lifecycleEvent.WorkflowInstanceId },
            Outcome = new AuditOutcome { Status = lifecycleEvent.EventType switch { "workflow.completed" => "succeeded", "workflow.failed" => "failed", _ => "indeterminate" }, Code = eventType == "workflow.failed" ? (string.IsNullOrWhiteSpace(lifecycleEvent.ReasonCode) ? "WORKFLOW_FAILED" : lifecycleEvent.ReasonCode) : null },
            Runtime = new AuditRuntimeContext { InvocationSource = "workflow", ExecutionId = lifecycleEvent.WorkflowRunOperationId, References = references.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray() },
            Descriptors = new AuditDescriptorContext { Items = [new AuditDescriptorReference { Kind = "workflow", Id = lifecycleEvent.WorkflowId, Version = lifecycleEvent.WorkflowVersion, ContractHash = lifecycleEvent.ContractHash }] },
            Evidence = [],
            Tags = AuditTagMap.Empty
        };
    }
}
