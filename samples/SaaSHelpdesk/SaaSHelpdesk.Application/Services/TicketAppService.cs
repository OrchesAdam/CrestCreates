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

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class TicketAppService : CrestAppServiceBase<Ticket, Guid, TicketDto, CreateTicketDto, UpdateTicketDto>, ITicketAppService
{
    private readonly ITicketRepository _ticketRepository;

    public TicketAppService(
        ITicketRepository repository,
        IServiceProvider serviceProvider,
        ICurrentUser currentUser,
        IDataPermissionFilter dataPermissionFilter,
        IPermissionChecker permissionChecker)
        : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _ticketRepository = repository;
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
        };
    }

    protected override void MapToEntity(UpdateTicketDto dto, Ticket entity)
    {
        entity.SetTitle(dto.Title);
        entity.SetDescription(dto.Description);
        entity.SetPriority(dto.Priority);
    }

    public async Task<TicketDto> AssignAsync(Guid id, Guid agentId)
    {
        var ticket = await Repository.GetAsync(id);
        ticket.AssignTo(agentId);
        await Repository.UpdateAsync(ticket);
        return MapToDto(ticket);
    }

    public async Task<TicketDto> ResolveAsync(Guid id)
    {
        var ticket = await Repository.GetAsync(id);
        ticket.Resolve();
        await Repository.UpdateAsync(ticket);
        return MapToDto(ticket);
    }

    public async Task<TicketDto> CloseAsync(Guid id)
    {
        var ticket = await Repository.GetAsync(id);
        ticket.Close();
        await Repository.UpdateAsync(ticket);
        return MapToDto(ticket);
    }

    public async Task<List<TicketDto>> GetByCustomerAsync(Guid customerId)
    {
        var tickets = await _ticketRepository.GetByCustomerAsync(customerId);
        return tickets.Select(MapToDto).ToList();
    }

    public async Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId)
    {
        var tickets = await _ticketRepository.GetByAssigneeAsync(assigneeId);
        return tickets.Select(MapToDto).ToList();
    }

    public async Task<List<TicketDto>> GetOverdueAsync()
    {
        var tickets = await _ticketRepository.GetOverdueTicketsAsync(DateTime.UtcNow);
        return tickets.Select(MapToDto).ToList();
    }
}
