using CrestCreates.Data.Abstractions;
using CrestCreates.Data.EFCore.UnitOfWork;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.PostgreSql;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore;

public sealed class ErpDeliveryStatusReader(PostgreSqlRuntimePersistenceOptions options,
    IDataBaseContext dataBaseContext, IEfCoreTransactionBorrower borrower) : IErpGoodsReceiptDelivery
{
    private readonly string _table = $"\"{options.Schema}\".runtime_outbox_messages";
    private const string ReceiptDelivery = """
        message_id = @id AND contract_id = @contract AND tenant_scope_kind = @scope AND tenant_id = @tenant
        """;

    public async Task<ErpDeliveryStatus?> GetAsync(Guid tenantId, Guid receiptId, CancellationToken cancellationToken)
    {
        var db = (ErpManagementDbContext)dataBaseContext.GetNativeContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var messageId = StockPostedEventPublisher.GetMessageId(receiptId);
        using var cancellation = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
        await db.Database.OpenConnectionAsync(effective);
        try
        {
            RequireFixedDatabase(connection);
            await using var command = new NpgsqlCommand($"""
                SELECT status, attempt_count, last_failure_code FROM {_table} WHERE {ReceiptDelivery}
                """, connection, (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
            BindDelivery(command, tenantId, messageId);
            await using var reader = await command.ExecuteReaderAsync(effective);
            if (!await reader.ReadAsync(effective))
                return null;

            var status = (OutboxDeliveryStatus)reader.GetInt32(0);
            var attempts = reader.GetInt32(1);
            var failure = reader.IsDBNull(2) ? null : reader.GetString(2);
            await reader.CloseAsync();
            var consumed = await IsConsumedAsync(db, tenantId, messageId, effective);
            return new ErpDeliveryStatus(status.ToString(), attempts, consumed, failure);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<ErpDeliveryStatus?> RetryAsync(Guid tenantId, Guid receiptId, CancellationToken cancellationToken)
    {
        if (!borrower.TryBorrowCurrent(out var context, out var transaction) ||
            context is not ErpManagementDbContext currentDb ||
            transaction?.GetDbTransaction() is not NpgsqlTransaction { Connection: { } connection } postgresTransaction)
        {
            throw new InvalidOperationException("ERP delivery retry requires the active managed ERP PostgreSQL transaction.");
        }

        RequireFixedDatabase(connection);
        var messageId = StockPostedEventPublisher.GetMessageId(receiptId);
        // Reset the attempt budget while preserving the immutable fact and its monotonic fence.
        await using var command = new NpgsqlCommand($"""
            UPDATE {_table}
               SET status = @pending, attempt_count = 0, available_at = clock_timestamp(), updated_at = clock_timestamp(),
                   dead_lettered_at = NULL, last_failure_code = NULL, last_failure_at = NULL,
                   terminal_lease_owner_id = NULL, terminal_fencing_token = NULL, terminal_failure_code = NULL
             WHERE {ReceiptDelivery}
               AND status = @dead AND lease_owner_id IS NULL AND lease_expires_at IS NULL
               AND terminal_fencing_token = fencing_token AND dead_lettered_at <= transaction_timestamp()
               AND terminal_failure_code IN ('ATTEMPT_BUDGET_EXHAUSTED', 'DELIVERY_ATTEMPT_BUDGET_EXHAUSTED')
            """, connection, postgresTransaction);
        BindDelivery(command, tenantId, messageId);
        command.Parameters.AddWithValue("pending", (int)OutboxDeliveryStatus.Pending);
        command.Parameters.AddWithValue("dead", (int)OutboxDeliveryStatus.DeadLettered);
        using var cancellation = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out var effective);
        if (await command.ExecuteNonQueryAsync(effective) != 1)
            return null;

        var consumed = await IsConsumedAsync(currentDb, tenantId, messageId, effective);
        return new ErpDeliveryStatus(nameof(OutboxDeliveryStatus.Pending), 0, consumed, null);
    }

    private static void BindDelivery(NpgsqlCommand command, Guid tenantId, string messageId)
    {
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("contract", ErpIntegrationWire.StockPostedId);
        command.Parameters.AddWithValue("scope", tenantId == Guid.Empty ? "host" : "tenant");
        command.Parameters.AddWithValue("tenant", tenantId == Guid.Empty ? string.Empty : tenantId.ToString());
    }

    private static Task<bool> IsConsumedAsync(ErpManagementDbContext context, Guid tenantId, string messageId,
        CancellationToken cancellationToken) => context.IntegrationEventInboxes.AnyAsync(x =>
        x.TenantId == tenantId && x.ConsumerId == ErpStockPostedInboxConsumer.ConsumerIdValue &&
        x.MessageId == messageId && x.ContractId == ErpIntegrationWire.StockPostedId, cancellationToken);

    private void RequireFixedDatabase(NpgsqlConnection connection)
    {
        var actual = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        var expected = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        if (actual.Host is null || actual.Host.Contains(',') ||
            !string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase) ||
            actual.Port != expected.Port ||
            !string.Equals(connection.Database, expected.Database ?? expected.Username, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ERP delivery requires the fixed PostgreSQL runtime database.");
        }
    }
}
