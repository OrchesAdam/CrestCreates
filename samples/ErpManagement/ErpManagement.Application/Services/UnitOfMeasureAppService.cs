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
    ContractType = typeof(IUnitOfMeasureAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/unit-of-measure",
    CapabilityIdPrefix = "compat.appservice.unit-of-measure")]
public class UnitOfMeasureAppService : CrestAppServiceBase<UnitOfMeasure, Guid, UnitOfMeasureDto, CreateUnitOfMeasureDto, UpdateUnitOfMeasureDto>, IUnitOfMeasureAppService
{
    private readonly ICurrentTenant _currentTenant;

    public UnitOfMeasureAppService(
        ICrestRepositoryBase<UnitOfMeasure, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override UnitOfMeasure MapToEntity(CreateUnitOfMeasureDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var unit = new UnitOfMeasure(Guid.NewGuid(), dto.Code, dto.Name, tenantId);
        unit.SetPrecision(dto.Precision);
        return unit;
    }

    protected override UnitOfMeasureDto MapToDto(UnitOfMeasure entity)
    {
        return new UnitOfMeasureDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Precision = entity.Precision,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateUnitOfMeasureDto dto, UnitOfMeasure entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetPrecision(dto.Precision);
    }
}