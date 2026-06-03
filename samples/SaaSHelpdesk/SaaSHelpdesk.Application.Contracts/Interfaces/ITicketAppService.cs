using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ITicketAppService
{
    Task<TicketDto> CreateAsync(CreateTicketDto input, CancellationToken cancellationToken = default);
    Task<TicketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TicketDto> UpdateAsync(Guid id, UpdateTicketDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
    Task<TicketDto> AssignAsync(Guid id, Guid agentId);
    Task<TicketDto> ResolveAsync(Guid id);
    Task<TicketDto> CloseAsync(Guid id);
    Task<List<TicketDto>> GetByCustomerAsync(Guid customerId);
    Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId);
    Task<List<TicketDto>> GetOverdueAsync();
}
