using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface ISalesOrderAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.SalesOrders.Create })]
    Task<SalesOrderDto> CreateAsync(CreateSalesOrderDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.SalesOrders.Get })]
    Task<SalesOrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.SalesOrders.Search })]
    Task<IReadOnlyList<SalesOrderDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.SalesOrders.Update })]
    Task<SalesOrderDto> UpdateAsync(Guid id, UpdateSalesOrderDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.SalesOrders.Delete },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/submit",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.SalesOrders.Submit })]
    Task<SalesOrderDto> SubmitAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/close",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.SalesOrders.Close })]
    Task<SalesOrderDto> CloseAsync(Guid id, CancellationToken cancellationToken = default);
}
