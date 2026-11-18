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
    ContractType = typeof(IWarehouseAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/warehouse",
    CapabilityIdPrefix = "compat.appservice.warehouse")]
public class WarehouseAppService : CrestAppServiceBase<Warehouse, Guid, WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>, IWarehouseAppService
{
    private readonly ICurrentTenant _currentTenant;

    public WarehouseAppService(
        ICrestRepositoryBase<Warehouse, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override Warehouse MapToEntity(CreateWarehouseDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var warehouse = new Warehouse(Guid.NewGuid(), dto.Code, dto.Name, tenantId);
        warehouse.SetAddress(dto.Address);
        warehouse.SetAllowNegativeStock(dto.AllowNegativeStock);
        return warehouse;
    }

    protected override WarehouseDto MapToDto(Warehouse entity)
    {
        return new WarehouseDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Address = entity.Address,
            AllowNegativeStock = entity.AllowNegativeStock,
            IsActive = entity.IsActive,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateWarehouseDto dto, Warehouse entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetAddress(dto.Address);
        entity.SetAllowNegativeStock(dto.AllowNegativeStock);
    }
}