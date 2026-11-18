using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class SettlementRecord : AuditedEntity<Guid>
{
    public Guid LedgerId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset SettledAt { get; private set; }
    public Guid TenantId { get; private set; }

    protected SettlementRecord() { }

    public SettlementRecord(Guid id, Guid ledgerId, decimal amount, DateTimeOffset at, Guid tenantId)
    {
        Id = id;
        LedgerId = ledgerId;
        Amount = amount;
        SettledAt = at;
        TenantId = tenantId;
    }
}
