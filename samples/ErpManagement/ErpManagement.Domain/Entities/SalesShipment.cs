using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SalesShipment : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public SalesShipmentStatus Status { get; private set; } = SalesShipmentStatus.Draft;
    public DateTimeOffset? PostedAt { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<SalesShipmentLine> Lines { get; private set; } = new List<SalesShipmentLine>();

    protected SalesShipment() { }

    public SalesShipment(Guid id, string number, Guid salesOrderId, Guid customerId, Guid warehouseId, Guid tenantId)
    {
        Id = id;
        SetNumber(number);
        SalesOrderId = salesOrderId;
        CustomerId = customerId;
        WarehouseId = warehouseId;
        TenantId = tenantId;
    }

    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
    }

    public void SetDraftDetails(SalesOrder order, Guid warehouseId, string? notes)
    {
        RequireDraft();
        SalesOrderId = order.Id;
        CustomerId = order.CustomerId;
        WarehouseId = warehouseId;
        Notes = notes;
    }

    public void RequireDraft()
    {
        if (Status != SalesShipmentStatus.Draft) throw new InvalidOperationException("Only draft shipments can be edited or posted.");
    }

    public void ReplaceLines(IEnumerable<SalesShipmentLine> lines)
    {
        RequireDraft();
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
    }

    public void Post(DateTimeOffset postedAt)
    {
        RequireDraft();
        if (Lines.Count == 0) throw new InvalidOperationException("A shipment without lines cannot be posted.");
        Status = SalesShipmentStatus.Posted;
        PostedAt = postedAt;
    }

}
