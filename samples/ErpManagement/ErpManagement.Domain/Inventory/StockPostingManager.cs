using CrestCreates.Domain.Repositories;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Inventory;

/// <summary>Single posting entry point. Balances may not be mutated anywhere else.</summary>
public sealed class StockPostingManager(
    ICrestRepositoryBase<StockBalance, Guid> balances,
    ICrestRepositoryBase<StockTransaction, Guid> transactions)
{
    public async Task<IReadOnlyList<StockMovementResult>> PostAsync(
        Guid tenantId,
        IReadOnlyList<StockMovementInput> movements,
        bool allowNegativeStock,
        string sourceDocType,
        Guid sourceDocId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<StockMovementResult>(movements.Count);
        foreach (var movement in movements)
        {
            var balance = balances.GetQueryable().FirstOrDefault(b =>
                b.TenantId == tenantId && b.MaterialId == movement.MaterialId
                && b.WarehouseId == movement.WarehouseId && b.StorageLocationId == movement.StorageLocationId);

            var isNew = balance is null;
            balance ??= new StockBalance(Guid.NewGuid(), movement.MaterialId, movement.WarehouseId, movement.StorageLocationId, tenantId);

            var newQuantity = balance.ApplyMovement(movement.SignedQuantity, allowNegativeStock);

            if (isNew)
                await balances.InsertAsync(balance, cancellationToken);
            else
                await balances.UpdateAsync(balance, cancellationToken);

            await transactions.InsertAsync(new StockTransaction(
                Guid.NewGuid(), movement.MaterialId, movement.WarehouseId, movement.StorageLocationId,
                movement.SignedQuantity, newQuantity, movement.MovementType, sourceDocType, sourceDocId, tenantId), cancellationToken);

            results.Add(new StockMovementResult(movement.MaterialId, movement.WarehouseId, movement.StorageLocationId, newQuantity, movement.MovementType));
        }

        return results;
    }
}

public readonly record struct StockMovementInput(
    Guid MaterialId, Guid WarehouseId, Guid StorageLocationId, decimal SignedQuantity, StockMovementType MovementType);

public readonly record struct StockMovementResult(
    Guid MaterialId, Guid WarehouseId, Guid StorageLocationId, decimal NewQuantity, StockMovementType MovementType);
