using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using Microsoft.AspNetCore.Http;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class CustomerPortalAppService : ICustomerPortalAppService
{
    private readonly ICrestRepositoryBase<Ticket, Guid> _ticketRepository;
    private readonly ICrestRepositoryBase<Customer, Guid> _customerRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CustomerPortalAppService(
        ICrestRepositoryBase<Ticket, Guid> ticketRepository,
        ICrestRepositoryBase<Customer, Guid> customerRepository,
        IHttpContextAccessor httpContextAccessor)
    {
        _ticketRepository = ticketRepository;
        _customerRepository = customerRepository;
        _httpContextAccessor = httpContextAccessor;
    }

    private Guid GetCurrentCustomerId()
    {
        var claim = _httpContextAccessor.HttpContext?.User.FindFirst("CustomerId");
        return claim != null ? Guid.Parse(claim.Value) : Guid.Empty;
    }

    public async Task<TicketDto> CreateTicketAsync(CreateTicketDto input)
    {
        var customerId = GetCurrentCustomerId();
        var ticket = new Ticket(Guid.NewGuid(), input.Title, input.Description,
            input.Priority, input.Type, customerId);
        await _ticketRepository.InsertAsync(ticket);
        return MapToDto(ticket);
    }

    public async Task<List<TicketDto>> GetMyTicketsAsync()
    {
        var customerId = GetCurrentCustomerId();
        var tickets = await _ticketRepository.GetListAsync(t => t.CustomerId == customerId);
        return tickets.Select(MapToDto).ToList();
    }

    public async Task<TicketDto?> GetTicketAsync(Guid id)
    {
        var ticket = await _ticketRepository.GetAsync(id);
        if (ticket == null || ticket.CustomerId != GetCurrentCustomerId())
            return null;
        return MapToDto(ticket);
    }

    private static TicketDto MapToDto(Ticket entity)
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
            CreationTime = entity.CreationTime,
        };
    }
}
