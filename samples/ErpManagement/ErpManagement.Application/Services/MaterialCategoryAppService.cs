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
    ContractType = typeof(IMaterialCategoryAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/material-category",
    CapabilityIdPrefix = "compat.appservice.material-category")]
public class MaterialCategoryAppService : CrestAppServiceBase<MaterialCategory, Guid, MaterialCategoryDto, CreateMaterialCategoryDto, UpdateMaterialCategoryDto>, IMaterialCategoryAppService
{
    private readonly ICurrentTenant _currentTenant;

    public MaterialCategoryAppService(
        ICrestRepositoryBase<MaterialCategory, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override MaterialCategory MapToEntity(CreateMaterialCategoryDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var category = new MaterialCategory(Guid.NewGuid(), dto.Code, dto.Name, tenantId);
        category.SetParent(dto.ParentId);
        category.SetSortOrder(dto.SortOrder);
        return category;
    }

    protected override MaterialCategoryDto MapToDto(MaterialCategory entity)
    {
        return new MaterialCategoryDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            ParentId = entity.ParentId,
            SortOrder = entity.SortOrder,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateMaterialCategoryDto dto, MaterialCategory entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetParent(dto.ParentId);
        entity.SetSortOrder(dto.SortOrder);
    }
}