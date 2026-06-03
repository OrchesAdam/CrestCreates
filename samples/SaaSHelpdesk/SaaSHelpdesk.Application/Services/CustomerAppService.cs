using System;
using System.Threading.Tasks;
using CrestCreates.Application.Services;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class CustomerAppService : CrestAppServiceBase<Customer, Guid, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerAppService
{
    private readonly ICurrentTenant _currentTenant;

    public CustomerAppService(
        ICrestRepositoryBase<Customer, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
    }

    protected override Customer MapToEntity(CreateCustomerDto dto)
    {
        // Fall back to Guid.Empty if tenant ID cannot be parsed as a valid Guid.
        // This ensures entity creation succeeds even when _currentTenant.Id is not a Guid-formatted string.
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var customer = new Customer(Guid.NewGuid(), dto.Name, dto.Email, tenantId);
        customer.SetPhone(dto.Phone);
        customer.SetCompany(dto.Company);
        return customer;
    }

    protected override CustomerDto MapToDto(Customer entity)
    {
        return new CustomerDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Email = entity.Email,
            Phone = entity.Phone,
            Company = entity.Company,
            IsActive = entity.IsActive,
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
        };
    }

    protected override void MapToEntity(UpdateCustomerDto dto, Customer entity)
    {
        entity.SetName(dto.Name);
        entity.SetPhone(dto.Phone);
        entity.SetCompany(dto.Company);
        entity.SetNotes(dto.Notes);
    }

    public async Task<CustomerDto> ActivateAsync(Guid id)
    {
        var customer = await Repository.GetAsync(id);
        if (customer.IsActive)
        {
            throw new InvalidOperationException($"Customer with id {id} is already active.");
        }
        customer.Activate();
        await Repository.UpdateAsync(customer);
        return MapToDto(customer);
    }

    public async Task<CustomerDto> DeactivateAsync(Guid id)
    {
        var customer = await Repository.GetAsync(id);
        if (!customer.IsActive)
        {
            throw new InvalidOperationException($"Customer with id {id} is already inactive.");
        }
        customer.Deactivate();
        await Repository.UpdateAsync(customer);
        return MapToDto(customer);
    }
}
