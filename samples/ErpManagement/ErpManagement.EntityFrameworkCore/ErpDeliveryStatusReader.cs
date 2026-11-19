using CrestCreates.Runtime.Persistence.PostgreSql;
using ErpManagement.Application.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore;

public sealed class ErpDeliveryStatusReader(NpgsqlDataSource dataSource, PostgreSqlRuntimePersistenceOptions options,
    ErpManagementDbContext db) : IErpDeliveryStatusReader
{
    public async Task<ErpDeliveryStatus?> GetAsync(string messageId, string consumerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT status, attempt_count, last_failure_code FROM "{options.Schema}".runtime_outbox_messages WHERE message_id=@id
            """, connection);
        command.Parameters.AddWithValue("id", messageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var status = reader.GetInt32(0) switch { 0 => "Pending", 1 => "InFlight", 2 => "Delivered", _ => "DeadLettered" };
        var attempts = reader.GetInt32(1);
        var failure = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.DisposeAsync();
        var consumed = await db.IntegrationEventInboxes.AnyAsync(x => x.ConsumerId == consumerId && x.MessageId == messageId, cancellationToken);
        return new ErpDeliveryStatus(status, attempts, consumed, failure);
    }
}
