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
    ContractType = typeof(IMaterialAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/material",
    CapabilityIdPrefix = "compat.appservice.material")]
public class MaterialAppService : CrestAppServiceBase<Material, Guid, MaterialDto, CreateMaterialDto, UpdateMaterialDto>, IMaterialAppService
{
    private readonly ICurrentTenant _currentTenant;

    public MaterialAppService(
        ICrestRepositoryBase<Material, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override Material MapToEntity(CreateMaterialDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var material = new Material(Guid.NewGuid(), dto.Code, dto.Name, dto.BaseUnitId, tenantId);
        material.SetSpecification(dto.Specification);
        material.SetMaterialType(dto.MaterialType);
        material.SetCategory(dto.CategoryId);
        material.SetDefaultWarehouse(dto.DefaultWarehouseId);
        material.SetSafetyStock(dto.SafetyStock);
        material.SetLeadTimeDays(dto.LeadTimeDays);
        material.SetNotes(dto.Notes);
        return material;
    }

    protected override MaterialDto MapToDto(Material entity)
    {
        return new MaterialDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Specification = entity.Specification,
            MaterialType = entity.MaterialType,
            CategoryId = entity.CategoryId,
            BaseUnitId = entity.BaseUnitId,
            DefaultWarehouseId = entity.DefaultWarehouseId,
            SafetyStock = entity.SafetyStock,
            LeadTimeDays = entity.LeadTimeDays,
            IsActive = entity.IsActive,
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateMaterialDto dto, Material entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetSpecification(dto.Specification);
        entity.SetMaterialType(dto.MaterialType);
        entity.SetCategory(dto.CategoryId);
        entity.SetBaseUnit(dto.BaseUnitId);
        entity.SetDefaultWarehouse(dto.DefaultWarehouseId);
        entity.SetSafetyStock(dto.SafetyStock);
        entity.SetLeadTimeDays(dto.LeadTimeDays);
        entity.SetNotes(dto.Notes);
    }
}