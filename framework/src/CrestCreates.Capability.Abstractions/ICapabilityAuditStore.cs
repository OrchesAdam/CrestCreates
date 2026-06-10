namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuditStore
{
    Task RecordAsync(CapabilityExecutionRecord record, CancellationToken ct = default);
}
