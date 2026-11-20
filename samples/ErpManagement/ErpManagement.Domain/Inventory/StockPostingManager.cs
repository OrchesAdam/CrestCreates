using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Entities.Auditing;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Domain.Inventory;

/// <summary>Single posting entry point. Balances may not be mutated anywhere else.</summary>
public sealed class StockPostingManager(
    ICrestRepositoryBase<StockBalance, Guid> balances,
    ICrestRepositoryBase<StockTransaction, Guid> transactions,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<StockMovementResult>> PostAsync(
        Guid tenantId,
        IReadOnlyList<StockMovementInput> movements,
        bool allowNegativeStock,
        string sourceDocType,
        Guid sourceDocId,
        CancellationToken cancellationToken = default,
        Guid? sourceOperationId = null)
    {
        StockTransaction.ValidateSource(sourceDocType, sourceDocId);
        var postedAt = DateTime.UtcNow;
        var actorId = Guid.TryParse(currentUser.Id, out var userId) ? userId : (Guid?)null;
        var results = new List<StockMovementResult>(movements.Count);
        var batchBalances = new Dictionary<(Guid MaterialId, Guid WarehouseId, Guid StorageLocationId), (StockBalance Balance, bool IsNew)>();
        foreach (var movement in movements)
        {
            var key = (movement.MaterialId, movement.WarehouseId, movement.StorageLocationId);
            if (!batchBalances.TryGetValue(key, out var entry))
            {
                var balance = await balances.GetAsync(b =>
                    b.TenantId == tenantId && b.MaterialId == key.MaterialId
                    && b.WarehouseId == key.WarehouseId && b.StorageLocationId == key.StorageLocationId, cancellationToken);
                entry = (balance ?? new StockBalance(Guid.NewGuid(), key.MaterialId, key.WarehouseId, key.StorageLocationId, tenantId), balance is null);
                batchBalances.Add(key, entry);
            }

            var newQuantity = entry.Balance.ApplyMovement(movement.SignedQuantity, allowNegativeStock);

            var transaction = new StockTransaction(
                Guid.NewGuid(), movement.MaterialId, movement.WarehouseId, movement.StorageLocationId,
                movement.SignedQuantity, newQuantity, movement.MovementType, sourceDocType, sourceDocId, tenantId, sourceOperationId);
            AuditPropertySetter.SetCreationAudit(transaction, postedAt, actorId);
            await transactions.InsertAsync(transaction, cancellationToken);

            results.Add(new StockMovementResult(movement.MaterialId, movement.WarehouseId, movement.StorageLocationId, newQuantity, movement.MovementType));
        }

        foreach (var (balance, isNew) in batchBalances.Values)
        {
            if (isNew)
            {
                AuditPropertySetter.SetCreationAudit(balance, postedAt, actorId);
                await balances.InsertAsync(balance, cancellationToken);
            }
            else
            {
                balance.LastModificationTime = postedAt;
                balance.LastModifierId = actorId;
                await balances.UpdateAsync(balance, cancellationToken);
            }
        }

        return results;
    }
}

public readonly record struct StockMovementInput(
    Guid MaterialId, Guid WarehouseId, Guid StorageLocationId, decimal SignedQuantity, StockMovementType MovementType);

public readonly record struct StockMovementResult(
    Guid MaterialId, Guid WarehouseId, Guid StorageLocationId, decimal NewQuantity, StockMovementType MovementType);
