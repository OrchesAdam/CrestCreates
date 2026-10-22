using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface IAgentAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Agent.Create" },
        RequiresValidator = true)]
    Task<IdentityUserDto> CreateAsync(CreateAgentDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Agent.Get" })]
    Task<IdentityUserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "deactivate",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Agent.Search" })]
    Task<IdentityUserDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
