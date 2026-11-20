using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

#pragma warning disable CS0618 // Implementation of the compatibility audit contract.
internal sealed class NullCapabilityAuditStore : ICapabilityAuditStore
#pragma warning restore CS0618
{
    public Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default)
        => Task.CompletedTask;
}
