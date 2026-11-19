using CrestCreates.Domain.Exceptions;
using ErpManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ErpManagement.EntityFrameworkCore.Repositories;

internal sealed class StockBalanceConflictInterceptor : SaveChangesInterceptor
{
    // Keep the physical name shipped by AddProcurementInventory.
    internal const string NaturalKeyIndexName = "IX_StockBalances_TenantId_MaterialId_WarehouseId_StorageLocati~";

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
        => TranslateConflict(eventData.Exception);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        TranslateConflict(eventData.Exception);
        return Task.CompletedTask;
    }

    private static void TranslateConflict(Exception exception)
    {
        if (exception is not DbUpdateException updateException ||
            updateException.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                TableName: "StockBalances",
                ConstraintName: NaturalKeyIndexName
            })
        {
            return;
        }

        var attemptedBalance = updateException.Entries.Select(entry => entry.Entity).OfType<StockBalance>().FirstOrDefault();
        throw new CrestConcurrencyException(nameof(StockBalance), attemptedBalance?.Id);
    }
}
