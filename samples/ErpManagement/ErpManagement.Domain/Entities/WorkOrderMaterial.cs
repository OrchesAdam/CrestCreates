using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class WorkOrderMaterial : AuditedEntity<Guid>
{
    public Guid WorkOrderId { get; private set; }
    public Guid MaterialId { get; private set; }
    public decimal RequiredQuantity { get; private set; }
    public decimal IssuedQuantity { get; private set; }
    public Guid TenantId { get; private set; }

    protected WorkOrderMaterial() { }

    public WorkOrderMaterial(Guid id, Guid workOrderId, Guid materialId, decimal issuedQuantity, Guid tenantId)
    {
        if (issuedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(issuedQuantity));
        Id = id;
        WorkOrderId = workOrderId;
        MaterialId = materialId;
        RequiredQuantity = issuedQuantity;
        IssuedQuantity = issuedQuantity;
        TenantId = tenantId;
    }
}
