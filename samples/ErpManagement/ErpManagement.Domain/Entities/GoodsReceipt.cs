using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class GoodsReceipt : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public Guid PurchaseOrderId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public GoodsReceiptStatus Status { get; private set; } = GoodsReceiptStatus.Draft;
    public DateTimeOffset? PostedAt { get; private set; }
    public DateTimeOffset? StockEventEmittedAt { get; private set; }
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<GoodsReceiptLine> Lines { get; private set; } = new List<GoodsReceiptLine>();

    protected GoodsReceipt() { }

    public GoodsReceipt(Guid id, string number, Guid purchaseOrderId, Guid supplierId, Guid warehouseId, Guid tenantId)
    {
        Id = id;
        SetNumber(number);
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        TenantId = tenantId;
    }

    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
    }

    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<GoodsReceiptLine> lines)
    {
        if (Status != GoodsReceiptStatus.Draft) throw new InvalidOperationException("Only draft receipts can be edited.");
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
    }

    public void Post(DateTimeOffset postedAt)
    {
        if (Status != GoodsReceiptStatus.Draft) throw new InvalidOperationException("Only draft receipts can be posted.");
        if (Lines.Count == 0) throw new InvalidOperationException("A receipt without lines cannot be posted.");
        Status = GoodsReceiptStatus.Posted;
        PostedAt = postedAt;
    }

    public void MarkStockEventEmitted(DateTimeOffset at) => StockEventEmittedAt = at;
}
