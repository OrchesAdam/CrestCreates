namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Persists agent tool invocation audit records.
/// Implementations MUST be idempotent by <see cref="AgentToolInvocationAuditRecord.AuditId"/>:
/// recording the same audit record (same AuditId) multiple times must not produce
/// duplicate entries. The <c>ExecuteAsync</c> pipeline and individual tool actions
/// may both call <see cref="RecordAsync"/> for the same invocation; the auditor
/// is responsible for deduplication.
/// </summary>
public interface IAgentToolInvocationAuditor
{
    /// <summary>
    /// Records an audit entry. Idempotent by AuditId — repeated calls with the
    /// same record must not duplicate the entry.
    /// </summary>
    Task RecordAsync(AgentToolInvocationAuditRecord record, CancellationToken ct = default);
}
