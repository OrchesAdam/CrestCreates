using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.EntityFrameworkCore;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Repositories;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.EntityFrameworkCore.Repositories;

public class TicketRepository : EfCoreRepository<Ticket, Guid>, ITicketRepository
{
    public TicketRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public async Task<List<Ticket>> GetOverdueTicketsAsync(DateTime referenceDate, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => t.Status != TicketStatus.Closed
                     && t.DueDate != null
                     && t.DueDate < referenceDate)
            .ToListAsync(ct);
    }

    public async Task<List<Ticket>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => t.CustomerId == customerId)
            .Include(t => t.Customer)
            .ToListAsync(ct);
    }

    public async Task<List<Ticket>> GetByAssigneeAsync(Guid assigneeId, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => t.AssigneeId == assigneeId)
            .ToListAsync(ct);
    }

    public async Task<List<Ticket>> GetByStatusAsync(TicketStatus status, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => t.Status == status)
            .ToListAsync(ct);
    }

    public async Task<List<Ticket>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => t.CreationTime >= start && t.CreationTime <= end)
            .ToListAsync(ct);
    }

    public async Task<List<Ticket>> GetSLABreachedTicketsAsync(DateTime referenceDate, CancellationToken ct = default)
    {
        return await GetQueryable()
            .Where(t => (t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress)
                     && t.DueDate != null
                     && t.DueDate < referenceDate)
            .ToListAsync(ct);
    }
}
