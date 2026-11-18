using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class WorkOrderReport : AuditedEntity<Guid>
{
    public Guid WorkOrderId { get; private set; }
    public decimal ReportedQuantity { get; private set; }
    public string ReportSource { get; private set; } = string.Empty;
    public DateTimeOffset ReportedAt { get; private set; }
    public Guid TenantId { get; private set; }

    protected WorkOrderReport() { }

    public WorkOrderReport(Guid id, Guid workOrderId, decimal quantity, string source, DateTimeOffset at, Guid tenantId)
    {
        Id = id;
        WorkOrderId = workOrderId;
        ReportedQuantity = quantity;
        ReportSource = source;
        ReportedAt = at;
        TenantId = tenantId;
    }
}
