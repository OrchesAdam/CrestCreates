using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IBomAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Bom.Create" },
        RequiresValidator = true)]
    Task<BomDto> CreateAsync(CreateBomDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Bom.Get" })]
    Task<BomDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Bom.Search" })]
    Task<IReadOnlyList<BomDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-material/{materialId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Bom.Search" })]
    Task<IReadOnlyList<BomDto>> GetByMaterialAsync(Guid materialId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Bom.Update" },
        RequiresValidator = true)]
    Task<BomDto> UpdateAsync(Guid id, UpdateBomDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "activate/{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Bom.Activate" })]
    Task<BomDto> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "disable/{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Bom.Activate" })]
    Task<BomDto> DisableAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Bom.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}