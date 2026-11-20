using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IGoodsReceiptAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Create })]
    Task<GoodsReceiptDto> CreateAsync(CreateGoodsReceiptDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Get })]
    Task<GoodsReceiptDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Search })]
    Task<IReadOnlyList<GoodsReceiptDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Update })]
    Task<GoodsReceiptDto> UpdateAsync(Guid id, UpdateGoodsReceiptDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Delete },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/post",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Post })]
    Task<GoodsReceiptDto> PostAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{id}/delivery-status", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Get })]
    Task<GoodsReceiptDeliveryStatusDto> GetDeliveryStatusAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{id}/retry-delivery", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.GoodsReceipts.Post })]
    Task<GoodsReceiptDeliveryStatusDto> RetryDeliveryAsync(Guid id, CancellationToken cancellationToken = default);
}