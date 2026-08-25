using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryWorkflowContinuationAcceptanceStore : IWorkflowContinuationAcceptanceStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    public InMemoryWorkflowContinuationAcceptanceStore(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;

    public Task<WorkflowContinuationAcceptanceWriteResult> AddAsync(WorkflowContinuationAcceptance acceptance, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ =>
        {
            var state = _coordinator.RequireAmbientState();
            var key = (acceptance.TenantScope, acceptance.CompletionEventId);
            if (state.ContinuationAcceptances.TryGetValue(key, out var existing))
            {
                if (existing.HumanTaskKey != acceptance.HumanTaskKey || existing.WorkflowKey != acceptance.WorkflowKey || existing.Integrity.Value != acceptance.Integrity.Value)
                    return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Conflict);
                return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Duplicate);
            }
            var byTask = state.ContinuationAcceptances.Values.FirstOrDefault(item => item.TenantScope == acceptance.TenantScope && item.HumanTaskKey == acceptance.HumanTaskKey);
            if (byTask is not null && byTask.CompletionEventId != acceptance.CompletionEventId)
                return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Conflict);
            state.ContinuationAcceptances[key] = acceptance;
            return ValueTask.FromResult(WorkflowContinuationAcceptanceWriteResult.Accepted);
        }, cancellationToken).AsTask();

    public Task<WorkflowContinuationAcceptance?> GetAsync(RuntimeTenantScope scope, string completionEventId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync<WorkflowContinuationAcceptance?>(_ =>
        {
            _coordinator.RequireAmbientState().ContinuationAcceptances.TryGetValue((scope, completionEventId), out var value);
            return ValueTask.FromResult(value);
        }, cancellationToken).AsTask();
}
