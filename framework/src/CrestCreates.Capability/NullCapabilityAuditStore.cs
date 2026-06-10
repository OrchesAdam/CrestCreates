using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

internal sealed class NullCapabilityAuditStore : ICapabilityAuditStore
{
    public Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;
}
