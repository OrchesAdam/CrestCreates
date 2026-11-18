using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IPurchaseRequisitionAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Create })]
    Task<PurchaseRequisitionDto> CreateAsync(CreatePurchaseRequisitionDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Get })]
    Task<PurchaseRequisitionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Search })]
    Task<IReadOnlyList<PurchaseRequisitionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Update })]
    Task<PurchaseRequisitionDto> UpdateAsync(Guid id, UpdatePurchaseRequisitionDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Delete },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/submit",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Submit })]
    Task<PurchaseRequisitionDto> SubmitAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "{id}/convert",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { ErpPermissions.PurchaseRequisitions.Convert })]
    Task<PurchaseOrderDto> ConvertAsync(Guid id, CancellationToken cancellationToken = default);
}