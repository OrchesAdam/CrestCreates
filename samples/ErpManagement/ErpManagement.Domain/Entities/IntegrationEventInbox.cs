using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;

namespace ErpManagement.Domain.Entities;

/// <summary>Inbox ledger proving reliable outbox consumption (idempotent by consumer + message).</summary>
[Entity]
public class IntegrationEventInbox : AuditedEntity<Guid>
{
    public string ConsumerId { get; private set; } = string.Empty;
    public string MessageId { get; private set; } = string.Empty;
    public string ContractId { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public Guid TenantId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;

    protected IntegrationEventInbox() { }

    public IntegrationEventInbox(Guid id, string consumerId, string messageId, string contractId, DateTimeOffset receivedAt, Guid tenantId, string payloadHash)
    {
        Id = id;
        ConsumerId = consumerId;
        MessageId = messageId;
        ContractId = contractId;
        ReceivedAt = receivedAt;
        CreationTime = receivedAt.UtcDateTime;
        TenantId = tenantId;
        PayloadHash = payloadHash;
    }
}
