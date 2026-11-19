using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Repositories;
using CrestCreates.Aop.Interceptors;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Integration;
using ErpManagement.Application.Events;
using ErpManagement.Application.Integration;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.MultiTenancy.Abstract;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ICustomerAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/customer",
    CapabilityIdPrefix = "compat.appservice.customer")]
public class CustomerAppService : CrestAppServiceBase<Customer, Guid, CustomerDto, CreateCustomerDto, UpdateCustomerDto>, ICustomerAppService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ErpIntegrationOutbox _outbox;

    public CustomerAppService(
        ICrestRepositoryBase<Customer, Guid> repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker,
        ICurrentTenant currentTenant,
        ICurrentPrincipalAccessor principalAccessor,
        ErpIntegrationOutbox outbox)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _currentTenant = currentTenant;
        _principalAccessor = principalAccessor;
        _outbox = outbox;
    }

    protected override Task CheckPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        if ((permissionName == CreatePermissionName || permissionName == UpdatePermissionName)
            && _principalAccessor.IsClient(_currentTenant, ErpIntegrationClients.Crm))
            return Task.CompletedTask;

        return base.CheckPermissionAsync(permissionName, cancellationToken);
    }

    public async Task<(CustomerDto Customer, bool Created)> UpsertFromCrmAsync(
        CreateCustomerDto input, decimal? creditLimit, CancellationToken cancellationToken)
    {
        if (!_principalAccessor.IsClient(_currentTenant, ErpIntegrationClients.Crm))
            throw new UnauthorizedAccessException("CRM integration credentials are required.");

        var tenantId = _currentTenant.GetErpTenantId();
        var existing = await Repository.GetAsync(customer => customer.TenantId == tenantId && customer.Code == input.Code,
            cancellationToken);
        if (existing is null)
            return (await CreateAsync(input, cancellationToken), true);

        return (await UpdateAsync(existing.Id, new UpdateCustomerDto
        {
            Code = existing.Code,
            Name = input.Name,
            ContactName = input.ContactName ?? existing.ContactName,
            Phone = input.Phone ?? existing.Phone,
            Email = input.Email ?? existing.Email,
            CreditLimit = creditLimit ?? existing.CreditLimit,
            Address = existing.Address,
            Notes = existing.Notes
        }, cancellationToken), false);
    }

    [UnitOfWorkMo]
    public override async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken cancellationToken = default)
    {
        var customer = await base.CreateAsync(input, cancellationToken);
        await PublishCustomerChangedAsync(customer, cancellationToken);
        return customer;
    }

    [UnitOfWorkMo]
    public override async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken cancellationToken = default)
    {
        var customer = await base.UpdateAsync(id, input, cancellationToken);
        await PublishCustomerChangedAsync(customer, cancellationToken);
        return customer;
    }

    private Task PublishCustomerChangedAsync(CustomerDto customer, CancellationToken cancellationToken)
        => _outbox.AppendAsync(new CrmCustomerChanged(customer.Id, customer.Code, customer.Name, _currentTenant.GetErpTenantId()),
            Guid.NewGuid(), DateTimeOffset.UtcNow, cancellationToken);

    protected override Customer MapToEntity(CreateCustomerDto dto)
    {
        var tenantId = Guid.TryParse(_currentTenant.Id, out var tid) ? tid : Guid.Empty;
        var customer = new Customer(Guid.NewGuid(), dto.Code, dto.Name, tenantId);
        customer.SetContactName(dto.ContactName);
        customer.SetPhone(dto.Phone);
        customer.SetEmail(dto.Email);
        customer.SetAddress(dto.Address);
        customer.SetCreditLimit(dto.CreditLimit);
        customer.SetNotes(dto.Notes);
        return customer;
    }

    protected override CustomerDto MapToDto(Customer entity)
    {
        return new CustomerDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            ContactName = entity.ContactName,
            Phone = entity.Phone,
            Email = entity.Email,
            Address = entity.Address,
            CreditLimit = entity.CreditLimit,
            IsActive = entity.IsActive,
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
            ConcurrencyStamp = entity.ConcurrencyStamp
        };
    }

    protected override void MapToEntity(UpdateCustomerDto dto, Customer entity)
    {
        entity.SetCode(dto.Code);
        entity.SetName(dto.Name);
        entity.SetContactName(dto.ContactName);
        entity.SetPhone(dto.Phone);
        entity.SetEmail(dto.Email);
        entity.SetAddress(dto.Address);
        entity.SetCreditLimit(dto.CreditLimit);
        entity.SetNotes(dto.Notes);
    }
}