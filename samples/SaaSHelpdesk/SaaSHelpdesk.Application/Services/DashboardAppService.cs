using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Application.Services;

[CrestService]
public class DashboardAppService : IDashboardAppService
{
    private readonly ICrestRepositoryBase<Ticket, Guid> _ticketRepository;

    public DashboardAppService(ICrestRepositoryBase<Ticket, Guid> ticketRepository)
    {
        _ticketRepository = ticketRepository;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        var today = DateTime.UtcNow.Date;
        var allTickets = await _ticketRepository.GetListAsync();

        var counted = allTickets.ToList();

        var byPriority = new Dictionary<string, int>();
        foreach (var p in Enum.GetValues<TicketPriority>())
            byPriority[p.ToString()] = 0;

        foreach (var t in counted)
        {
            byPriority[t.Priority.ToString()]++;
        }

        return new DashboardSummaryDto
        {
            TotalTickets = counted.Count,
            OpenTickets = counted.Count(t => t.Status == TicketStatus.Open),
            InProgressTickets = counted.Count(t => t.Status == TicketStatus.InProgress),
            ResolvedTickets = counted.Count(t => t.Status == TicketStatus.Resolved),
            ClosedTickets = counted.Count(t => t.Status == TicketStatus.Closed),
            OverdueTickets = counted.Count(t => t.DueDate.HasValue && t.DueDate.Value.Date < today && t.Status != TicketStatus.Closed),
            TicketsCreatedToday = counted.Count(t => t.CreationTime.Date >= today),
            TicketsByPriority = byPriority,
        };
    }
}
