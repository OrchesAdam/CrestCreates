using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IStockTransferAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.StockTransfers.Create })]
    Task<StockTransferDto> CreateAsync(CreateStockTransferDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.StockTransfers.Get })]
    Task<StockTransferDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.StockTransfers.Search })]
    Task<IReadOnlyList<StockTransferDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.StockTransfers.Update })]
    Task<StockTransferDto> UpdateAsync(Guid id, UpdateStockTransferDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.StockTransfers.Delete },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/post",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.StockTransfers.Post })]
    Task<StockTransferDto> PostAsync(Guid id, CancellationToken cancellationToken = default);
}