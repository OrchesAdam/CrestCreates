using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Contracts.Interfaces;

public interface ICategoryAppService
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Category.Create" },
        RequiresValidator = true)]
    Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Get" })]
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Category.Update" })]
    Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Category.Delete" },
        RequiresConcurrencyToken = true)]
    Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "tree",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    Task<List<CategoryDto>> GetTreeAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "roots",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    Task<List<CategoryDto>> GetRootsAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "children/{parentId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    Task<List<CategoryDto>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "move",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Category.Search" })]
    Task<CategoryDto> MoveAsync(Guid id, Guid? newParentId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "reorder",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Category.Search" })]
    Task ReorderAsync(Guid id, int sortOrder, CancellationToken cancellationToken = default);
}
