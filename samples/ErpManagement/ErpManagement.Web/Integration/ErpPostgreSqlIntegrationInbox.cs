using ErpManagement.Application.Events;
using ErpManagement.Domain.Entities;
using ErpManagement.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ErpManagement.Web.Integration;

internal sealed class ErpPostgreSqlIntegrationInbox(ErpManagementDbContext db) : IErpIntegrationInbox
{
    private const string DeliveryKey = "IX_IntegrationEventInboxes_ConsumerId_MessageId";

    public async Task<ErpInboxWriteResult> RecordAsync(string consumerId, string messageId, string contractId,
        Guid tenantId, string payloadHash, CancellationToken cancellationToken)
    {
        var receipt = new IntegrationEventInbox(Guid.NewGuid(), consumerId, messageId, contractId,
            DateTimeOffset.UtcNow, tenantId, payloadHash);
        db.IntegrationEventInboxes.Add(receipt);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ErpInboxWriteResult.Inserted;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: DeliveryKey })
        {
            db.Entry(receipt).State = EntityState.Detached;
            var winner = await db.IntegrationEventInboxes.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ConsumerId == consumerId && row.MessageId == messageId,
                    cancellationToken).ConfigureAwait(false);
            if (winner is null)
                throw;

            return winner.TenantId == tenantId && winner.ContractId == contractId && winner.PayloadHash == payloadHash
                ? ErpInboxWriteResult.Duplicate
                : ErpInboxWriteResult.Conflict;
        }
    }
}
