using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ITicketAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Ticket.Create" })]
    Task<TicketDto> CreateAsync(CreateTicketDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Ticket.Get" })]
    Task<TicketDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Ticket.Search" })]
    Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Ticket.Update" })]
    Task<TicketDto> UpdateAsync(Guid id, UpdateTicketDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Ticket.Delete" })]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "assign",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Ticket.Update" })]
    Task<TicketDto> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "resolve",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Ticket.Update" })]
    Task<TicketDto> ResolveAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "close",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Ticket.Update" })]
    Task<TicketDto> CloseAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-customer/{customerId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Ticket.Get" })]
    Task<List<TicketDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-assignee/{assigneeId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Ticket.Get" })]
    Task<List<TicketDto>> GetByAssigneeAsync(Guid assigneeId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "overdue",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Ticket.Search" })]
    Task<List<TicketDto>> GetOverdueAsync(CancellationToken cancellationToken = default);
}
