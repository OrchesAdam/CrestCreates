using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class WorkOrder : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public Guid MaterialId { get; private set; }
    public decimal Quantity { get; private set; }
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Planned;
    public Guid? SourceSalesOrderId { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }
    public virtual ICollection<WorkOrderMaterial> Materials { get; private set; } = new List<WorkOrderMaterial>();
    public virtual ICollection<WorkOrderReport> Reports { get; private set; } = new List<WorkOrderReport>();

    protected WorkOrder() { }

    public WorkOrder(Guid id, string number, Guid tenantId)
    {
        Id = id;
        Number = number;
        TenantId = tenantId;
    }

    public void Plan(Guid materialId, decimal quantity, Guid? sourceSalesOrderId, string? notes)
    {
        RequirePlanned();
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        MaterialId = materialId;
        Quantity = quantity;
        SourceSalesOrderId = sourceSalesOrderId;
        Notes = notes;
    }

    public void RequirePlanned()
    {
        if (Status != WorkOrderStatus.Planned) throw new InvalidOperationException("Only planned work orders may be edited or released.");
    }

    public void Release(IEnumerable<WorkOrderMaterial> materials)
    {
        RequirePlanned();
        foreach (var material in materials) Materials.Add(material);
        Status = WorkOrderStatus.Released;
    }

    public WorkOrderReport AddReport(decimal quantity, string source, Guid operationId)
    {
        if (Status is not (WorkOrderStatus.Released or WorkOrderStatus.InProgress))
            throw new InvalidOperationException("Only released or in-progress work orders accept reports.");
        var total = Reports.Sum(report => report.ReportedQuantity) + quantity;
        if (quantity <= 0 || total > Quantity) throw new ArgumentOutOfRangeException(nameof(quantity));
        var report = new WorkOrderReport(Guid.NewGuid(), Id, quantity, source, DateTimeOffset.UtcNow, TenantId, operationId);
        Reports.Add(report);
        Status = total == Quantity ? WorkOrderStatus.Completed : WorkOrderStatus.InProgress;
        return report;
    }

    public void Close()
    {
        if (Status != WorkOrderStatus.Completed) throw new InvalidOperationException("Only completed work orders can be closed.");
        Status = WorkOrderStatus.Closed;
    }
}
