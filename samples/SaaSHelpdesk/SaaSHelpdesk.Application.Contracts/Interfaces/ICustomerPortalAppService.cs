using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICustomerPortalAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "create-ticket",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "CustomerPortal.Search" },
        RequiresValidator = true)]
    Task<TicketDto> CreateTicketAsync(CreateTicketDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "my-tickets",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "CustomerPortal.Search" })]
    Task<List<TicketDto>> GetMyTicketsAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "ticket/{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "CustomerPortal.Search" })]
    Task<TicketDto?> GetTicketAsync(Guid id, CancellationToken cancellationToken = default);
}
