using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Entities;

[Entity]
public class ArApLedger : AuditedEntity<Guid>
{
    public ArApDirection Direction { get; private set; }
    public string SourceDocType { get; private set; } = string.Empty;
    public Guid SourceDocId { get; private set; }
    public Guid PartyId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal SettledAmount { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public ArApStatus Status { get; private set; } = ArApStatus.Open;
    public Guid TenantId { get; private set; }
    public virtual ICollection<SettlementRecord> Settlements { get; private set; } = new List<SettlementRecord>();

    protected ArApLedger() { }

    public ArApLedger(Guid id, ArApDirection direction, string sourceDocType, Guid sourceDocId,
        Guid partyId, decimal amount, Guid tenantId, DateTimeOffset? dueDate = null)
    {
        Id = id;
        Direction = direction;
        SourceDocType = sourceDocType;
        SourceDocId = sourceDocId;
        PartyId = partyId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        DueDate = dueDate;
        TenantId = tenantId;
        Status = Amount == 0 ? ArApStatus.Settled : ArApStatus.Open;
    }

    public void Settle(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > Amount - SettledAmount)
            throw new CrestCreates.Domain.Shared.Exceptions.CrestValidationException("Settlement must be positive, use at most two decimals and not exceed the outstanding balance.");
        SettledAmount += amount;
        Settlements.Add(new SettlementRecord(Guid.NewGuid(), Id, amount, DateTimeOffset.UtcNow, TenantId));
        Status = SettledAmount == Amount ? ArApStatus.Settled : ArApStatus.PartiallySettled;
    }
}
