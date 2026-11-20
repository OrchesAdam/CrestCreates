using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SettlementRecord : AuditedEntity<Guid>
{
    private string? _operationResult;

    public Guid LedgerId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset SettledAt { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? OperationId { get; private set; }

    protected SettlementRecord() { }

    public SettlementRecord(Guid id, Guid ledgerId, decimal amount, DateTimeOffset at, Guid tenantId, Guid operationId)
    {
        Id = id;
        LedgerId = ledgerId;
        Amount = amount;
        SettledAt = at;
        TenantId = tenantId;
        OperationId = operationId;
    }

    public void RecordOperationResult(string result) => _operationResult = result;

    public string GetOperationResult() => _operationResult
        ?? throw new InvalidOperationException("The settlement has no stored operation result.");
}
