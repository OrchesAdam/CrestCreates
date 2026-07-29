namespace CrestCreates.Capability.Abstractions;

[Obsolete("Append-only compatibility API. Use IAuditRecorder and a contract-compliant IAuditSink for Accountability facts.")]
public interface ICapabilityAuditStore
{
    Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default);
}
