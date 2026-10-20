using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Application.Services;
using CrestCreates.Aop.Interceptors;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Repositories;
using SaaSHelpdesk.Domain.Shared.Enums;
using System.Threading;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ITicketAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/ticket",
    CapabilityIdPrefix = "compat.appservice.ticket")]
public class TicketAppService : CrestAppServiceBase<Ticket, Guid, TicketDto, CreateTicketDto, UpdateTicketDto>, ITicketAppService
{
    protected override bool RequiresConcurrencyStampOnUpdate => true;

    protected override bool RequiresConcurrencyStampOnDelete => true;

    private const string UpdatePermission = "Ticket.Update";
    private const string GetPermission = "Ticket.Get";
    private const string SearchPermission = "Ticket.Search";

    private readonly ITicketRepository _ticketRepository;
    private readonly ICrestRepositoryBase<SLAPolicy, Guid> _slaPolicyRepository;

    public TicketAppService(
        ITicketRepository repository,
        ICrestRepositoryBase<SLAPolicy, Guid> slaPolicyRepository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _ticketRepository = repository;
        _slaPolicyRepository = slaPolicyRepository;
    }

    protected override Ticket MapToEntity(CreateTicketDto dto)
    {
        return new Ticket(Guid.NewGuid(), dto.Title, dto.Description, dto.Priority, dto.Type, dto.CustomerId);
    }

    protected override TicketDto MapToDto(Ticket entity)
    {
        return new TicketDto
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Status = entity.Status,
            Priority = entity.Priority,
            Type = entity.Type,
            CustomerId = entity.CustomerId,
            AssigneeId = entity.AssigneeId,
            CategoryId = entity.CategoryId,
            DueDate = entity.DueDate,
            ResolvedAt = entity.ResolvedAt,
            ClosedAt = entity.ClosedAt,
            IsEscalated = entity.IsEscalated,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            ConcurrencyStamp = entity.ConcurrencyStamp,
        };
    }

    protected override void MapToEntity(UpdateTicketDto dto, Ticket entity)
    {
        entity.SetTitle(dto.Title);
        entity.SetDescription(dto.Description);
        entity.SetPriority(dto.Priority);
        entity.SetCategory(dto.CategoryId);
    }

    [UnitOfWorkMo]
    public async Task<TicketDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermission, cancellationToken);
        var ticket = await Repository.GetAsync(id, cancellationToken);
        if (ticket == null)
            throw new KeyNotFoundException($"Ticket with id {id} not found.");
        ticket.AssignTo(agentId);

        var activeSlaPolicy = _slaPolicyRepository.GetQueryable()
            .FirstOrDefault(p => p.IsActive);
        if (activeSlaPolicy is not null)
        {
            var responseMinutes = activeSlaPolicy.GetResponseMinutes(ticket.Priority);
            ticket.SetDueDate(DateTime.UtcNow.AddMinutes(responseMinutes));
        }

        await Repository.UpdateAsync(ticket, cancellationToken);
        return MapToDto(ticket);
    }

    [UnitOfWorkMo]
    public async Task<TicketDto> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermission, cancellationToken);
        var ticket = await Repository.GetAsync(id, cancellationToken);
        if (ticket == null)
            throw new KeyNotFoundException($"Ticket with id {id} not found.");
        if (ticket.Status == TicketStatus.Closed)
            throw new InvalidOperationException("Cannot resolve a ticket that is already closed.");
        ticket.Resolve();
        await Repository.UpdateAsync(ticket, cancellationToken);
        return MapToDto(ticket);
    }

    [UnitOfWorkMo]
    public async Task<TicketDto> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(UpdatePermission, cancellationToken);
        var ticket = await Repository.GetAsync(id, cancellationToken);
        if (ticket == null)
            throw new KeyNotFoundException($"Ticket with id {id} not found.");
        if (ticket.Status == TicketStatus.Closed)
            throw new InvalidOperationException("Cannot close a ticket that is already closed.");
        ticket.Close();
        await Repository.UpdateAsync(ticket, cancellationToken);
        return MapToDto(ticket);
    }

    public async Task<List<TicketDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(GetPermission, cancellationToken);
        var tickets = await _ticketRepository.GetByCustomerAsync(customerId, cancellationToken);
        return tickets.Select(MapToDto).ToList();
    }

    public async Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId, CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(GetPermission, cancellationToken);
        var tickets = await _ticketRepository.GetByAssigneeAsync(assigneeId, cancellationToken);
        return tickets.Select(MapToDto).ToList();
    }

    public async Task<List<TicketDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
    {
        await CheckPermissionAsync(SearchPermission, cancellationToken);
        var tickets = await _ticketRepository.GetOverdueTicketsAsync(DateTime.UtcNow, cancellationToken);
        return tickets.Select(MapToDto).ToList();
    }
}
