using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class StockTransfer : AuditedEntity<Guid>
{
    public string Number { get; private set; } = string.Empty;
    public StockTransferStatus Status { get; private set; } = StockTransferStatus.Draft;
    public string? Notes { get; private set; }
    public Guid TenantId { get; private set; }

    public virtual ICollection<StockTransferLine> Lines { get; private set; } = new List<StockTransferLine>();

    protected StockTransfer() { }

    public StockTransfer(Guid id, string number, Guid tenantId)
    {
        Id = id;
        if (string.IsNullOrWhiteSpace(number) || number.Length > 64) throw new ArgumentException("Number must be 1..64 chars", nameof(number));
        Number = number;
        TenantId = tenantId;
    }

    public void SetNotes(string? notes) => Notes = notes;

    public void ReplaceLines(IEnumerable<StockTransferLine> lines)
    {
        if (Status != StockTransferStatus.Draft) throw new InvalidOperationException("Only draft transfers can be edited.");
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
    }

    public void Post()
    {
        if (Status != StockTransferStatus.Draft) throw new InvalidOperationException("Only draft transfers can be posted.");
        if (Lines.Count == 0) throw new InvalidOperationException("A transfer without lines cannot be posted.");
        Status = StockTransferStatus.Posted;
    }
}
