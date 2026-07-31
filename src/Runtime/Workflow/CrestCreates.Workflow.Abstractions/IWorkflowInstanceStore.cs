namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowInstanceStore
{
    Task AddAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkflowInstance instance, long expectedRevision, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeInstanceKey key,
        CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(
        CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeInstanceKey humanTaskKey,
        CancellationToken cancellationToken = default);
}
