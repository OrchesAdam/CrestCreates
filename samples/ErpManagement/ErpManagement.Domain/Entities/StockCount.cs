using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockCount : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public Guid WarehouseId { get; private set; }
    public StockCountStatus Status { get; private set; } = StockCountStatus.Draft;
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<StockCountLine> Lines { get; private set; } = new List<StockCountLine>();

    protected StockCount() { }

    public StockCount(Guid id, string number, Guid warehouseId, Guid tenantId)
    {
        Id = id;
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
        WarehouseId = warehouseId;
        TenantId = tenantId;
    }

    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<StockCountLine> lines)
    {
        if (Status != StockCountStatus.Draft) throw new InvalidOperationException("Only draft counts can be edited.");
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
    }

    public void Post()
    {
        if (Status != StockCountStatus.Draft) throw new InvalidOperationException("Only draft counts can be posted.");
        if (Lines.Count == 0) throw new InvalidOperationException("A count without lines cannot be posted.");
        Status = StockCountStatus.Posted;
    }
}
