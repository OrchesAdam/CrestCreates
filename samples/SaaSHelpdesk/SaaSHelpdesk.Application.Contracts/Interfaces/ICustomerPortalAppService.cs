using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICustomerPortalAppService
{
    Task<TicketDto> CreateTicketAsync(CreateTicketDto input);
    Task<List<TicketDto>> GetMyTicketsAsync();
    Task<TicketDto?> GetTicketAsync(Guid id);
}
