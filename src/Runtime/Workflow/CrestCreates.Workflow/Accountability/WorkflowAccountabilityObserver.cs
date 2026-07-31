using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow.Accountability;

internal sealed class WorkflowAccountabilityObserver : IWorkflowLifecycleObserver
{
    private readonly IAuditRecorder _recorder;

    public WorkflowAccountabilityObserver(IAuditRecorder recorder)
    {
        _recorder = recorder;
    }

    public async ValueTask ObserveAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default)
    {
        var references = new List<AuditRuntimeReference>
        {
            new("workflow-instance", lifecycleEvent.WorkflowInstanceId)
        };
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.WorkflowRunOperationId))
            references.Add(new("workflow-run-operation", lifecycleEvent.WorkflowRunOperationId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.StepId))
            references.Add(new("workflow-step", lifecycleEvent.StepId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.HumanTaskInstanceId))
            references.Add(new("human-task-instance", lifecycleEvent.HumanTaskInstanceId));
        if (!string.IsNullOrWhiteSpace(lifecycleEvent.HumanTaskCompletionEventId))
            references.Add(new("event-instance", lifecycleEvent.HumanTaskCompletionEventId));

        var initiatingActor = lifecycleEvent.Origin?.InitiatingActor;

        var actor = new AuditActor
        {
            Kind = "workflow",
            Id = lifecycleEvent.WorkflowInstanceId,
            InitiatedBy = initiatingActor is null
                ? null
                : new AuditActorReference(initiatingActor.Kind, initiatingActor.Id)
        };
        var eventType = lifecycleEvent.EventType switch
        {
            "workflow.started" => "workflow.started",
            "workflow.suspended" => "workflow.suspended",
            "workflow.resumed" => "workflow.resumed",
            "workflow.completed" => "workflow.completed",
            "workflow.failed" => "workflow.failed",
            _ => throw new InvalidOperationException($"Unsupported workflow lifecycle event '{lifecycleEvent.EventType}'.")
        };
        var envelope = new AuditEnvelope
        {
            AuditId = lifecycleEvent.AuditId,
            OccurredAt = lifecycleEvent.OccurredAt,
            TenantId = lifecycleEvent.TenantId,
            CorrelationId = lifecycleEvent.CorrelationId ?? lifecycleEvent.Origin?.CorrelationId ?? string.Empty,
            CausationId = lifecycleEvent.CausationId,
            ParentAuditId = lifecycleEvent.ParentAuditId,
            PreviousAuditId = lifecycleEvent.PreviousAuditId,
            Actor = actor,
            Action = new AuditAction { Kind = "workflow.lifecycle", Name = eventType },
            Target = new AuditTarget { Kind = "workflow-instance", Id = lifecycleEvent.WorkflowInstanceId },
            Outcome = new AuditOutcome
            {
                Status = lifecycleEvent.EventType switch
                {
                    "workflow.completed" => "succeeded",
                    "workflow.failed" => "failed",
                    _ => "indeterminate"
                },
                Code = eventType == "workflow.failed"
                    ? (string.IsNullOrWhiteSpace(lifecycleEvent.ReasonCode) ? "WORKFLOW_FAILED" : lifecycleEvent.ReasonCode)
                    : null
            },
            Runtime = new AuditRuntimeContext
            {
                InvocationSource = "workflow",
                ExecutionId = lifecycleEvent.WorkflowRunOperationId,
                References = references.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray()
            },
            Descriptors = new AuditDescriptorContext
            {
                Items = [new AuditDescriptorReference { Kind = "workflow", Id = lifecycleEvent.WorkflowId, Version = lifecycleEvent.WorkflowVersion, ContractHash = lifecycleEvent.ContractHash }]
            },
            Evidence = [],
            Tags = AuditTagMap.Empty
        };
        await _recorder.RecordAsync(envelope, CancellationToken.None).ConfigureAwait(false);
    }

}
