using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// In-memory activation auditor for Phase 7e.
/// Not for production use — no durable persistence.
/// </summary>
public sealed class InMemoryDescriptorActivationAuditor : IDescriptorActivationAuditor
{
    private readonly ConcurrentQueue<DescriptorActivationAuditRecord> _records = new();

    public Task RecordAsync(DescriptorActivationAuditRecord record, CancellationToken ct = default)
    {
        _records.Enqueue(record);
        return Task.CompletedTask;
    }

    public IReadOnlyList<DescriptorActivationAuditRecord> GetAllRecords()
        => _records.OrderBy(r => r.Timestamp).ThenBy(r => r.AuditRecordId).ToList().AsReadOnly();
}
