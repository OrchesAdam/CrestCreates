using System.Security.Cryptography;
using System.Text;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

/// <summary>
/// Owns the only Runtime transaction which makes a HumanTask wait visible.
/// Step executors prepare detached candidates; they never persist them.
/// </summary>
internal sealed class WorkflowSuspensionCommitter
{
    private readonly IRuntimeTransactionCoordinator _transactions;
    private readonly IWorkflowInstanceStore _workflows;
    private readonly IHumanTaskInstanceStore _humanTasks;
    private readonly IWorkflowSuspensionReceiptStore _receipts;
    private readonly IDescriptorSnapshotStore? _snapshots;

    public WorkflowSuspensionCommitter(
        IRuntimeTransactionCoordinator transactions,
        IWorkflowInstanceStore workflows,
        IHumanTaskInstanceStore humanTasks,
        IWorkflowSuspensionReceiptStore receipts,
        IDescriptorSnapshotStore? snapshots = null)
    {
        _transactions = transactions;
        _workflows = workflows;
        _humanTasks = humanTasks;
        _receipts = receipts;
        _snapshots = snapshots;
    }

    public async Task CommitAsync(
        WorkflowInstance workflowBefore,
        WorkflowInstance suspendedWorkflow,
        HumanTaskInstance preparedHumanTask,
        string suspensionOperationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowBefore);
        ArgumentNullException.ThrowIfNull(suspendedWorkflow);
        ArgumentNullException.ThrowIfNull(preparedHumanTask);
        ArgumentException.ThrowIfNullOrWhiteSpace(suspensionOperationId);

        ValidateCorrelation(workflowBefore, suspendedWorkflow, preparedHumanTask);
        var receipt = CreateReceipt(workflowBefore, suspendedWorkflow, preparedHumanTask, suspensionOperationId);

        await _transactions.ExecuteAsync(async ct =>
        {
            await RuntimeDescriptorPinEvidence.ValidateAsync(suspendedWorkflow.WorkflowPin, _snapshots, ct).ConfigureAwait(false);
            await RuntimeDescriptorPinEvidence.ValidateAsync(preparedHumanTask.HumanTaskPin, _snapshots, ct).ConfigureAwait(false);

            var write = await _receipts.AddAsync(receipt, ct).ConfigureAwait(false);
            if (write.Status == WorkflowSuspensionReceiptWriteStatus.Conflict)
            {
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                    "A conflicting suspension receipt already exists for this operation.");
            }
            if (write.Status == WorkflowSuspensionReceiptWriteStatus.Duplicate)
            {
                if (!ReceiptMatches(receipt, write.Receipt)
                    || !await SuspensionFactsAreVisibleAsync(receipt, ct).ConfigureAwait(false))
                {
                    throw new RuntimePersistenceContractException(
                        RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                        "A suspension receipt does not prove the matching durable suspension facts.");
                }
                return;
            }

            await _humanTasks.AddAsync(preparedHumanTask, ct).ConfigureAwait(false);
            await _workflows.UpdateAsync(suspendedWorkflow, workflowBefore.Revision, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool ReceiptMatches(WorkflowSuspensionReceipt expected, WorkflowSuspensionReceipt actual)
        => expected.Scope == actual.Scope
            && string.Equals(expected.SuspensionOperationId, actual.SuspensionOperationId, StringComparison.Ordinal)
            && expected.WorkflowKey == actual.WorkflowKey
            && expected.HumanTaskKey == actual.HumanTaskKey
            && expected.WorkflowFromRevision == actual.WorkflowFromRevision
            && expected.WorkflowToRevision == actual.WorkflowToRevision
            && expected.WorkflowPin == actual.WorkflowPin
            && expected.HumanTaskPin == actual.HumanTaskPin
            && expected.Integrity == actual.Integrity;

    private async Task<bool> SuspensionFactsAreVisibleAsync(
        WorkflowSuspensionReceipt receipt,
        CancellationToken cancellationToken)
    {
        var workflow = await _workflows.GetAsync(receipt.WorkflowKey, cancellationToken).ConfigureAwait(false);
        if (workflow is null
            || workflow.Status != WorkflowInstanceStatus.Suspended
            || workflow.WaitingHumanTaskKey != receipt.HumanTaskKey
            || workflow.Revision != receipt.WorkflowToRevision)
        {
            return false;
        }

        var task = await _humanTasks.GetAsync(receipt.HumanTaskKey, cancellationToken).ConfigureAwait(false);
        return task is not null
            && task.WorkflowKey == receipt.WorkflowKey
            && task.HumanTaskPin == receipt.HumanTaskPin;
    }

    private static void ValidateCorrelation(
        WorkflowInstance workflowBefore,
        WorkflowInstance suspendedWorkflow,
        HumanTaskInstance preparedHumanTask)
    {
        workflowBefore.Key.EnsureValid();
        suspendedWorkflow.Key.EnsureValid();
        preparedHumanTask.Key.EnsureValid();
        if (workflowBefore.Key != suspendedWorkflow.Key
            || workflowBefore.Revision != suspendedWorkflow.Revision
            || suspendedWorkflow.Status != WorkflowInstanceStatus.Suspended
            || suspendedWorkflow.WaitingHumanTaskKey != preparedHumanTask.Key
            || preparedHumanTask.WorkflowKey != workflowBefore.Key
            || !string.Equals(workflowBefore.TenantId, preparedHumanTask.TenantId, StringComparison.Ordinal))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow and HumanTask suspension correlation must be reciprocal and tenant-local.");
        }
    }

    private static WorkflowSuspensionReceipt CreateReceipt(
        WorkflowInstance workflowBefore,
        WorkflowInstance suspendedWorkflow,
        HumanTaskInstance humanTask,
        string operationId)
    {
        var scope = new RuntimeTenantScope(workflowBefore.TenantId);
        return new WorkflowSuspensionReceipt
        {
            Scope = scope,
            SuspensionOperationId = operationId,
            WorkflowKey = workflowBefore.Key,
            HumanTaskKey = humanTask.Key,
            WorkflowFromRevision = workflowBefore.Revision,
            WorkflowToRevision = workflowBefore.Revision + 1,
            WorkflowPin = suspendedWorkflow.WorkflowPin,
            HumanTaskPin = humanTask.HumanTaskPin,
            Integrity = BuildIntegrity(scope, operationId, workflowBefore, suspendedWorkflow, humanTask)
        };
    }

    private static CanonicalHash BuildIntegrity(
        RuntimeTenantScope scope,
        string operationId,
        WorkflowInstance before,
        WorkflowInstance after,
        HumanTaskInstance task)
    {
        var text = string.Join("\n",
            scope.TenantId ?? "<host>",
            operationId,
            before.Key.InstanceId,
            task.Key.InstanceId,
            before.Revision,
            before.Revision + 1,
            PinText(after.WorkflowPin),
            PinText(task.HumanTaskPin),
            StateText(after.Variables),
            StateText(after.StepVariables),
            task.Input?.TypeId ?? "<none>",
            task.Input?.SchemaRef?.FullId ?? "<none>",
            task.Input?.JsonPayload ?? "<none>");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new CanonicalHash
        {
            Value = digest,
            Algorithm = "SHA-256",
            AlgorithmVersion = "runtime-suspension-v1",
            ArtifactKind = "RuntimeSuspension",
            Scope = "InternalFull",
            Purpose = "Integrity",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "runtime-suspension-v1"
        };
    }

    private static string PinText(CrestCreates.Metadata.Abstractions.Runtime.RuntimeDescriptorPin pin)
        => string.Join("|", pin.Ref.Namespace, pin.Ref.Id, pin.Ref.Version, HashText(pin.ContractHash), HashText(pin.DefinitionHash), pin.SnapshotId ?? "");

    private static string HashText(CanonicalHash hash)
        => string.Join("|", hash.Value, hash.Algorithm, hash.AlgorithmVersion, hash.ArtifactKind, hash.DescriptorKind ?? "", hash.Scope, hash.Purpose, hash.ContractVersion, hash.CanonicalShapeVersion);

    private static string StateText(IReadOnlyDictionary<string, CrestCreates.Runtime.Persistence.Abstractions.State.RuntimeStateValue> values)
        => string.Join("\n", values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => string.Join("|", x.Key, x.Value.TypeId, x.Value.SchemaRef?.FullId ?? "", x.Value.JsonPayload)));
}
