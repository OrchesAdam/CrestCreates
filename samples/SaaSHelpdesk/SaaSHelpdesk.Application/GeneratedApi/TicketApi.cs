using CrestCreates.DynamicApi;
using SaaSHelpdesk.Application.Contracts.DTOs;
using SaaSHelpdesk.Application.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace SaaSHelpdesk.Application.GeneratedApi;

[GeneratedApiController("api/ticket")]
public partial class TicketApi : CrestApiController
{
    private readonly ITicketAppService _ticketAppService;

    public TicketApi(ITicketAppService ticketAppService)
    {
        _ticketAppService = ticketAppService;
    }

    [HttpGet("all")]
    [ApiOverride(CrudAction.GetList)]
    public Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _ticketAppService.GetAllAsync(cancellationToken);
    }
}
