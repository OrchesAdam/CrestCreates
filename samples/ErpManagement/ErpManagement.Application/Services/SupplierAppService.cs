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
    ContractType = typeof(ISupplierAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/supplier",
    CapabilityIdPrefix = "compat.appservice.supplier")]
public class SupplierAppService : CrestAppServiceBase<Supplier, Guid, SupplierDto, CreateSupplierDto, UpdateSupplierDto>, ISupplierAppService
{
    private readonly ICurrentTenant _currentTenant;

    public SupplierAppService(
        ICrestRepositoryBase<Supplier, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override Supplier MapToEntity(CreateSupplierDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var supplier = new Supplier(Guid.NewGuid(), dto.Code, dto.Name, tenantId);
        supplier.SetContactName(dto.ContactName);
        supplier.SetPhone(dto.Phone);
        supplier.SetEmail(dto.Email);
        supplier.SetAddress(dto.Address);
        supplier.SetPaymentTermsDays(dto.PaymentTermsDays);
        supplier.SetNotes(dto.Notes);
        return supplier;
    }

    protected override SupplierDto MapToDto(Supplier entity)
    {
        return new SupplierDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            ContactName = entity.ContactName,
            Phone = entity.Phone,
            Email = entity.Email,
            Address = entity.Address,
            PaymentTermsDays = entity.PaymentTermsDays,
            IsActive = entity.IsActive,
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateSupplierDto dto, Supplier entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetContactName(dto.ContactName);
        entity.SetPhone(dto.Phone);
        entity.SetEmail(dto.Email);
        entity.SetAddress(dto.Address);
        entity.SetPaymentTermsDays(dto.PaymentTermsDays);
        entity.SetNotes(dto.Notes);
    }
}