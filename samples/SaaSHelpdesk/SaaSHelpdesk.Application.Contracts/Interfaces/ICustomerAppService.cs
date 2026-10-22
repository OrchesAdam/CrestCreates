using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICustomerAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Customer.Create" },
        RequiresValidator = true)]
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Customer.Get" })]
    Task<CustomerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Customer.Search" })]
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Customer.Update" },
        RequiresValidator = true)]
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Customer.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "activate",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Customer.Search" })]
    Task<CustomerDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "deactivate",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Customer.Search" })]
    Task<CustomerDto> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
