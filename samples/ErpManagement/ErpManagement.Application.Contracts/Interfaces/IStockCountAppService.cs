using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IStockCountAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.StockCounts.Create })]
    Task<StockCountDto> CreateAsync(CreateStockCountDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.StockCounts.Get })]
    Task<StockCountDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.StockCounts.Search })]
    Task<IReadOnlyList<StockCountDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.StockCounts.Update })]
    Task<StockCountDto> UpdateAsync(Guid id, UpdateStockCountDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.StockCounts.Delete },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/post",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.StockCounts.Post })]
    Task<StockCountDto> PostAsync(Guid id, CancellationToken cancellationToken = default);
}