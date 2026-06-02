using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ITicketAppService
{
    Task<TicketDto> AssignAsync(Guid id, Guid agentId);
    Task<TicketDto> ResolveAsync(Guid id);
    Task<TicketDto> CloseAsync(Guid id);
    Task<List<TicketDto>> GetByCustomerAsync(Guid customerId);
    Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId);
    Task<List<TicketDto>> GetOverdueAsync();
}
