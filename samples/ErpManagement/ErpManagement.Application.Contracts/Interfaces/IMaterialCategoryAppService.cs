using CrestCreates.Domain.Shared.Attributes;
using ErpManagement.Application.Contracts.DTOs;

namespace ErpManagement.Application.Contracts.Interfaces;

public interface IMaterialCategoryAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "MaterialCategory.Create" },
        RequiresValidator = true)]
    Task<MaterialCategoryDto> CreateAsync(CreateMaterialCategoryDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "MaterialCategory.Get" })]
    Task<MaterialCategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "MaterialCategory.Search" })]
    Task<IReadOnlyList<MaterialCategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "MaterialCategory.Update" },
        RequiresValidator = true)]
    Task<MaterialCategoryDto> UpdateAsync(Guid id, UpdateMaterialCategoryDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "MaterialCategory.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);
}