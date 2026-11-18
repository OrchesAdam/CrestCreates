using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IStorageLocationAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "StorageLocation.Create" },
        RequiresValidator = true)]
    Task<StorageLocationDto> CreateAsync(CreateStorageLocationDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "StorageLocation.Get" })]
    Task<StorageLocationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "StorageLocation.Search" })]
    Task<IReadOnlyList<StorageLocationDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-warehouse/{warehouseId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "StorageLocation.Search" })]
    Task<IReadOnlyList<StorageLocationDto>> GetByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "StorageLocation.Update" },
        RequiresValidator = true)]
    Task<StorageLocationDto> UpdateAsync(Guid id, UpdateStorageLocationDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "StorageLocation.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}