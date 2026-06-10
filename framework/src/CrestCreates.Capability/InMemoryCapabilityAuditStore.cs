using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryCapabilityAuditStore : ICapabilityAuditStore
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
