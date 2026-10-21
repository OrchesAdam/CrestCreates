using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Projections;

[CapabilityCompatibilityProjection(
    ContractType = typeof(CategoryHttpProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/category",
    CapabilityIdPrefix = "compat.appservice.category")]
public sealed class CategoryHttpProjection(ICategoryAppService service)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Category.Create" })]
    public Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken cancellationToken = default)
        => service.CreateAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Get" })]
    public Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => service.GetByIdAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "all",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    public Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => service.GetAllAsync(cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    public Task<PagedResultDto<CategoryDto>> GetListAsync(
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => service.GetListAsync(
            new PagedRequestDto { PageIndex = pageIndex ?? 0, PageSize = pageSize ?? 0 },
            cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "query",
        Kind = HttpProjectionOperationKind.Query,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Category.Search" })]
    public Task<PagedResultDto<CategoryDto>> QueryAsync(
        QueryRequest<Category> request,
        CancellationToken cancellationToken = default)
        => service.QueryAsync(request, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Category.Update" })]
    public Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryDto input, CancellationToken cancellationToken = default)
        => service.UpdateAsync(id, input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Category.Delete" },
        RequiresConcurrencyToken = true)]
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
        => service.DeleteAsync(id, expectedStamp, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "exists/{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => service.ExistsAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "count",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Category.Search" })]
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => service.CountAsync(cancellationToken);
}
