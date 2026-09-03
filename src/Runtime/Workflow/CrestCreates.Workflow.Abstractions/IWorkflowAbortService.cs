using CrestCreates.Runtime.Persistence.Abstractions.Keys;

namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Canonical authority for aborting a Workflow which is suspended on a
/// HumanTask. The Workflow and HumanTask terminal writes share one Runtime
/// transaction and the abort emits the normal Workflow failed lifecycle fact.
/// </summary>
public interface IWorkflowAbortService
{
    Task<WorkflowAbortResult> AbortAsync(
        RuntimeInstanceKey workflowKey,
        RuntimeInstanceKey humanTaskKey,
        string reason,
        string abortOperationId,
        CancellationToken cancellationToken = default);
}
