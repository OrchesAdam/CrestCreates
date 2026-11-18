using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IStorageLocationAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/storage-location",
    CapabilityIdPrefix = "compat.appservice.storage-location")]
public class StorageLocationAppService : CrestAppServiceBase<StorageLocation, Guid, StorageLocationDto, CreateStorageLocationDto, UpdateStorageLocationDto>, IStorageLocationAppService
{
    private readonly ICurrentTenant _currentTenant;

    public StorageLocationAppService(
        ICrestRepositoryBase<StorageLocation, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<StorageLocationDto>> GetByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermissionName, cancellationToken);
        var query = Repository.GetQueryable();
        query = await ApplyDataPermissionFilterAsync(query);
        return query.Where(l => l.WarehouseId == warehouseId).ToList().Select(MapToDto).ToList();
    }

    protected override StorageLocation MapToEntity(CreateStorageLocationDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        return new StorageLocation(Guid.NewGuid(), dto.WarehouseId, dto.Code, dto.Name, tenantId);
    }

    protected override StorageLocationDto MapToDto(StorageLocation entity)
    {
        return new StorageLocationDto
        {
            Id = entity.Id,
            WarehouseId = entity.WarehouseId,
            Code = entity.Code,
            Name = entity.Name,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateStorageLocationDto dto, StorageLocation entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
    }
}