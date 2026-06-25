namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Auditor for descriptor activation lifecycle events.
/// Phase 7e local scope — not the full Accountability Runtime.
/// </summary>
public interface IDescriptorActivationAuditor
{
    Task RecordAsync(DescriptorActivationAuditRecord record, CancellationToken ct = default);

    IReadOnlyList<DescriptorActivationAuditRecord> GetAllRecords();
}
