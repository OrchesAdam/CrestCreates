using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Services;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.CodeGenerator.Tests.Entities;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;

namespace CrestCreates.CodeGenerator.Tests.Services;

[CrestService]
public class TestOrderAppService : CrestAppServiceBase<TestOrder, long, TestOrderDto, CreateTestOrderDto, UpdateTestOrderDto>
{
    public TestOrderAppService(
        ICrestRepositoryBase<TestOrder, long> repository,
        IMapper mapper,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, mapper, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
    }

    [UnitOfWorkMo]
    public async Task<TestOrderDto> ConfirmOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        await CheckEntityPermissionAsync("Confirm", cancellationToken);

        var order = await Repository.GetAsync(id, cancellationToken);
        if (order == null)
        {
            throw new Exception($"Order with id {id} not found");
        }

        order.ConfirmOrder();
        await Repository.UpdateAsync(order, cancellationToken);

        return Mapper.Map<TestOrderDto>(order);
    }

    [UnitOfWorkMo]
    public async Task<TestOrderDto> ShipOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        await CheckEntityPermissionAsync("Ship", cancellationToken);

        var order = await Repository.GetAsync(id, cancellationToken);
        if (order == null)
        {
            throw new Exception($"Order with id {id} not found");
        }

        order.ShipOrder();
        await Repository.UpdateAsync(order, cancellationToken);

        return Mapper.Map<TestOrderDto>(order);
    }

    [UnitOfWorkMo]
    public async Task<TestOrderDto> CompleteOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        await CheckEntityPermissionAsync("Complete", cancellationToken);

        var order = await Repository.GetAsync(id, cancellationToken);
        if (order == null)
        {
            throw new Exception($"Order with id {id} not found");
        }

        order.CompleteOrder();
        await Repository.UpdateAsync(order, cancellationToken);

        return Mapper.Map<TestOrderDto>(order);
    }

    [UnitOfWorkMo]
    public async Task<TestOrderDto> CancelOrderAsync(long id, CancellationToken cancellationToken = default)
    {
        await CheckEntityPermissionAsync("Cancel", cancellationToken);

        var order = await Repository.GetAsync(id, cancellationToken);
        if (order == null)
        {
            throw new Exception($"Order with id {id} not found");
        }

        order.CancelOrder();
        await Repository.UpdateAsync(order, cancellationToken);

        return Mapper.Map<TestOrderDto>(order);
    }
}

public class TestOrderDto
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime OrderDate { get; set; }
    public int Status { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class CreateTestOrderDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }
}

public class UpdateTestOrderDto
{
    public string? Notes { get; set; }
}
