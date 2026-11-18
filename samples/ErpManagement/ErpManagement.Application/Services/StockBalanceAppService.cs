using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

/// <summary>Read-only projection: balances are mutated only by the StockPostingManager kernel.</summary>
[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IStockBalanceAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/stock-balances",
    CapabilityIdPrefix = "compat.appservice.stock-balances")]
public class StockBalanceAppService : IStockBalanceAppService
{
    private readonly ICrestRepositoryBase<StockBalance, Guid> _repository;
    private readonly IPermissionChecker _permissionChecker;

    public StockBalanceAppService(
        ICrestRepositoryBase<StockBalance, Guid> repository,
        IPermissionChecker permissionChecker)
    {
        _repository = repository;
        _permissionChecker = permissionChecker;
    }

    public async Task<IReadOnlyList<StockBalanceDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.StockBalances.Search);
        var balances = await _repository.GetListAsync(cancellationToken);
        return balances.Select(MapToDto).ToList();
    }

    public async Task<StockBalanceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.StockBalances.Get);
        var balance = await _repository.GetAsync(id, cancellationToken);
        return balance == null ? null : MapToDto(balance);
    }

    private static StockBalanceDto MapToDto(StockBalance entity) => new()
    {
        Id = entity.Id,
        MaterialId = entity.MaterialId,
        WarehouseId = entity.WarehouseId,
        StorageLocationId = entity.StorageLocationId,
        Quantity = entity.Quantity,
        TenantId = entity.TenantId,
        CreationTime = entity.CreationTime,
        ConcurrencyStamp = entity.ConcurrencyStamp
    };
}