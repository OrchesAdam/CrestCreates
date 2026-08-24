using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Bootstrap;

internal sealed class InMemoryHumanTaskCompletionObligationPreflight(InMemoryRuntimeTransactionCoordinator coordinator) : IHumanTaskCompletionObligationPreflight
{
    public ValueTask ValidateAsync(IReadOnlyList<HumanTaskCompletionObligationPolicyRegistration> policies, IReadOnlySet<string> activeConsumerIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var task in coordinator.CurrentState.HumanTasks.Values)
        {
            if (task.Status == HumanTaskInstanceStatus.CompletionDispatchFailed)
                throw new InvalidOperationException($"Legacy HumanTask '{task.Key.InstanceId}' is in CompletionDispatchFailed and must be explicitly reconciled before transactional outbox cutover.");
            if (task.Status is not (HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned))
                continue;
            foreach (var policy in policies.Where(policy => policy.HumanTaskDescriptorId == task.HumanTaskPin.Ref.Id && policy.HumanTaskDescriptorVersion == task.HumanTaskPin.Ref.Version))
            {
                if (!activeConsumerIds.Contains(policy.RequiredConsumerId))
                    throw new InvalidOperationException($"HumanTask obligation consumer '{policy.RequiredConsumerId}' is not registered.");
                if (!task.RequiredCompletionConsumerIds.Contains(policy.RequiredConsumerId, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Active HumanTask '{task.Key.InstanceId}' is missing completion obligation '{policy.RequiredConsumerId}'.");
            }
        }
        return ValueTask.CompletedTask;
    }
}
