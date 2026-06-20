using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// In-memory audit record store with AuditId deduplication.
/// Sufficient for Phase 7c scope.
/// Field-compatible with a future CrestCreates.Accountability audit envelope
/// but does not force that module into 7c.
///
/// Deduplication ensures that when the ExecuteAsync pipeline records an
/// audit that was already persisted by the tool action, the record is
/// not duplicated. This is safe because both writes use the same
/// AuditRecord instance (same AuditId).
/// </summary>
public sealed class InMemoryAgentToolInvocationAuditor : IAgentToolInvocationAuditor
{
    private readonly ConcurrentBag<AgentToolInvocationAuditRecord> _records = new();
    private readonly ConcurrentDictionary<string, bool> _recordedIds = new(StringComparer.Ordinal);

    public Task RecordAsync(AgentToolInvocationAuditRecord record, CancellationToken ct = default)
    {
        // Dedup by AuditId — the same audit record may be recorded by both
        // the tool action and the ExecuteAsync pipeline. Only the first write
        // is retained.
        if (_recordedIds.TryAdd(record.AuditId, true))
        {
            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<AgentToolInvocationAuditRecord> GetAllRecords()
        => _records.ToArray();

    public IReadOnlyList<AgentToolInvocationAuditRecord> GetRecordsByToolName(string toolName)
        => _records.Where(r => r.Context.ToolName == toolName).ToArray();

    public IReadOnlyList<AgentToolInvocationAuditRecord> GetRecordsByTenant(string tenantId)
        => _records.Where(r => r.Context.TenantId == tenantId).ToArray();

    public IReadOnlyList<AgentToolInvocationAuditRecord> GetRecordsByCorrelationId(string correlationId)
        => _records.Where(r => r.Context.CorrelationId == correlationId).ToArray();

    public void Clear()
    {
        _records.Clear();
        _recordedIds.Clear();
    }
}
