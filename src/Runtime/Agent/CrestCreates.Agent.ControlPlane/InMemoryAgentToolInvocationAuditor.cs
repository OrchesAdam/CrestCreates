using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// In-memory audit record store. Sufficient for Phase 7c scope.
/// Field-compatible with a future CrestCreates.Accountability audit envelope
/// but does not force that module into 7c.
/// </summary>
public sealed class InMemoryAgentToolInvocationAuditor : IAgentToolInvocationAuditor
{
    private readonly ConcurrentBag<AgentToolInvocationAuditRecord> _records = new();

    public Task RecordAsync(AgentToolInvocationAuditRecord record, CancellationToken ct = default)
    {
        _records.Add(record);
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

    public void Clear() => _records.Clear();
}
