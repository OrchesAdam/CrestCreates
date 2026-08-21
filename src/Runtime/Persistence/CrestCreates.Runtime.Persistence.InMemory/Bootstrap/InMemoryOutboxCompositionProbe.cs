using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Bootstrap;

internal sealed class InMemoryOutboxCompositionProbe(InMemoryRuntimeTransactionCoordinator coordinator) : IOutboxCompositionProbe
{
    public ValueTask ValidateAsync(ActiveOutboxRequirements requirements, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = coordinator.CurrentState;
        foreach (var record in state.Outbox.Values)
        {
            if (!requirements.ContractIds.Contains(record.Message.Metadata.ContractId))
                throw new OutboxCompositionException($"Outbox contract '{record.Message.Metadata.ContractId}' is not registered.");

            foreach (var consumerId in record.Message.Metadata.RequiredConsumerIds)
            {
                if (!requirements.ConsumerIds.Contains(consumerId))
                    throw new OutboxCompositionException($"Outbox required consumer '{consumerId}' is not registered.");
            }
        }
        return ValueTask.CompletedTask;
    }
}
