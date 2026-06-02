using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using SaaSHelpdesk.Domain.Entities;
using SaaSHelpdesk.Domain.Shared.Enums;

namespace SaaSHelpdesk.Domain.Repositories;

public interface ITicketRepository : ICrestRepositoryBase<Ticket, Guid>
{
    /// <summary>
    /// Get tickets that are overdue (past DueDate and not closed).
    /// </summary>
    Task<List<Ticket>> GetOverdueTicketsAsync(DateTime referenceDate, CancellationToken ct = default);

    /// <summary>
    /// Get all tickets for a specific customer.
    /// </summary>
    Task<List<Ticket>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Get tickets assigned to a specific agent.
    /// </summary>
    Task<List<Ticket>> GetByAssigneeAsync(Guid assigneeId, CancellationToken ct = default);

    /// <summary>
    /// Get tickets with a specific status.
    /// </summary>
    Task<List<Ticket>> GetByStatusAsync(TicketStatus status, CancellationToken ct = default);

    /// <summary>
    /// Get tickets created within a date range.
    /// </summary>
    Task<List<Ticket>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default);

    /// <summary>
    /// Get tickets that have breached SLA (open/inprogress with passed DueDate).
    /// </summary>
    Task<List<Ticket>> GetSLABreachedTicketsAsync(DateTime referenceDate, CancellationToken ct = default);
}
