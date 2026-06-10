namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Upsert semantics. No INSERT/UPDATE database semantics in the abstraction.
/// </summary>
public interface IWorkflowInstanceStore
{
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default);
}
