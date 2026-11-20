using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

/// <summary>Read-only projection: transactions are immutable facts appended by the posting kernel.</summary>
[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IStockTransactionAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/stock-transactions",
    CapabilityIdPrefix = "compat.appservice.stock-transactions")]
public class StockTransactionAppService : IStockTransactionAppService
{
    private readonly ICrestRepositoryBase<StockTransaction, Guid> _repository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentTenant _currentTenant;

    public StockTransactionAppService(
        ICrestRepositoryBase<StockTransaction, Guid> repository,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
    {
        _repository = repository;
        _permissionChecker = permissionChecker;
        _currentTenant = currentTenant;
    }

    private Guid CurrentTenantId => Guid.TryParse(_currentTenant.Id, out var id) ? id : Guid.Empty;

    public async Task<IReadOnlyList<StockTransactionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.StockTransactions.Search);
        var tenantId = CurrentTenantId;
        var transactions = await _repository.GetListAsync(t => t.TenantId == tenantId, cancellationToken);
        return transactions.Select(MapToDto).ToList();
    }

    public async Task<StockTransactionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.StockTransactions.Get);
        var tenantId = CurrentTenantId;
        var transaction = await _repository.GetAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);
        return transaction == null ? null : MapToDto(transaction);
    }

    private static StockTransactionDto MapToDto(StockTransaction entity) => new()
    {
        Id = entity.Id,
        MaterialId = entity.MaterialId,
        WarehouseId = entity.WarehouseId,
        StorageLocationId = entity.StorageLocationId,
        SignedQuantity = entity.SignedQuantity,
        BalanceAfter = entity.BalanceAfter,
        MovementType = entity.MovementType,
        SourceDocType = entity.SourceDocType,
        SourceDocId = entity.SourceDocId,
        SourceOperationId = entity.SourceOperationId,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        ConcurrencyStamp = entity.ConcurrencyStamp
    };
}