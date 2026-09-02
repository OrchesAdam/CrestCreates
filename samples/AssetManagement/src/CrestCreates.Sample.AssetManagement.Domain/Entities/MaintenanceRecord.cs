using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Sample.AssetManagement.Domain.Entities;

[Entity]
public sealed class MaintenanceRecord : MustHaveTenantOrganizationEntity<Guid>
{
    public Guid AssetId { get; private set; }
    public string WorkflowInstanceId { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public string CompletedBy { get; private set; } = string.Empty;
    public string Note { get; private set; } = string.Empty;
    public bool Approved { get; private set; }
    public DateTime CompletedAt { get; private set; }

    private MaintenanceRecord() { }

    public MaintenanceRecord(
        Guid id,
        string tenantId,
        Guid assetId,
        Guid? organizationId,
        string workflowInstanceId,
        string requestedBy,
        string completedBy,
        string note,
        bool approved)
    {
        Id = id;
        TenantId = tenantId;
        AssetId = assetId;
        OrganizationId = organizationId;
        WorkflowInstanceId = workflowInstanceId;
        RequestedBy = requestedBy;
        CompletedBy = completedBy;
        Note = note;
        Approved = approved;
        CompletedAt = DateTime.UtcNow;
    }
}
