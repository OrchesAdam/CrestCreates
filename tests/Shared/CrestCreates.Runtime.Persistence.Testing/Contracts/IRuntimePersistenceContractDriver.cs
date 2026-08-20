using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Runtime.Persistence.Testing.Contracts;

/// <summary>
/// Provider adapter consumed by the provider-neutral Runtime contract cases.
/// The adapter owns lifecycle/setup; the cases own observable semantics.
/// </summary>
public interface IRuntimePersistenceContractDriver
{
    string ScopeId { get; }
    IRuntimeTransactionCoordinator Transactions { get; }
    IWorkflowInstanceStore Workflows { get; }
    IHumanTaskInstanceStore HumanTasks { get; }
    IDescriptorSnapshotStore Snapshots { get; }
    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}
