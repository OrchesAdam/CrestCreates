using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class PurchaseOrder : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public Guid? RequisitionId { get; private set; }
    public Guid SupplierId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public decimal TotalAmount { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<PurchaseOrderLine> Lines { get; private set; } = new List<PurchaseOrderLine>();

    protected PurchaseOrder() { }

    public PurchaseOrder(Guid id, string number, Guid supplierId, Guid tenantId)
    {
        Id = id;
        SetNumber(number);
        SupplierId = supplierId;
        TenantId = tenantId;
    }

    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
    }

    public void SetRequisition(Guid? requisitionId) => RequisitionId = requisitionId;
    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<PurchaseOrderLine> lines)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft orders can be edited.");
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
        TotalAmount = Lines.Sum(line => line.Amount);
    }

    public void Confirm()
    {
        if (Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft orders can be confirmed.");
        if (Lines.Count == 0) throw new InvalidOperationException("An order without lines cannot be confirmed.");
        Status = PurchaseOrderStatus.Confirmed;
    }

    public void RecordReceipt(IReadOnlyDictionary<Guid, decimal> receivedByLine)
    {
        if (Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived))
            throw new InvalidOperationException("Order is not open for receiving.");
        foreach (var line in Lines)
        {
            if (receivedByLine.TryGetValue(line.Id, out var qty)) line.AddReceived(qty);
        }
        Status = Lines.All(line => line.ReceivedQuantity >= line.Quantity)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }

    public void Close()
    {
        if (Status is PurchaseOrderStatus.Closed) throw new InvalidOperationException("Order is already closed.");
        Status = PurchaseOrderStatus.Closed;
    }
}
