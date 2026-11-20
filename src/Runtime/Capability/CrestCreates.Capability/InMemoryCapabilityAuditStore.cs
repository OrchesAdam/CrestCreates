using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

#pragma warning disable CS0618 // Implementation of the compatibility audit contract.
public sealed class InMemoryCapabilityAuditStore : ICapabilityAuditStore
#pragma warning restore CS0618
{
    private readonly ConcurrentQueue<CapabilityExecutionRecord> _records = new();

    public Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default)
    {
        _records.Enqueue(record);
        return Task.CompletedTask;
    }

    public IReadOnlyList<CapabilityExecutionRecord> GetRecords() => _records.ToList();
    public void Clear() => _records.Clear();
}
