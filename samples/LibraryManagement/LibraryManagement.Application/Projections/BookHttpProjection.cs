using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;

namespace LibraryManagement.Application.Projections;

[CapabilityCompatibilityProjection(
    ContractType = typeof(BookHttpProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/book",
    CapabilityIdPrefix = "compat.appservice.book")]
public sealed class BookHttpProjection(IBookAppService service)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "by-isbn/{isbn}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Book.Get" })]
    public Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
        => service.GetByIsbnAsync(isbn, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "search-books",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Book.Search" })]
    public Task<PagedResultDto<BookDto>> SearchBooksAsync(
        string? keyword,
        string? author,
        int? minCopies,
        string? sortField,
        bool? sortDescending,
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => service.SearchBooksAsync(
            keyword,
            author,
            minCopies,
            sortField,
            sortDescending ?? false,
            pageIndex ?? 0,
            pageSize ?? 0,
            cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Post,
        Route = "",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Book.Create" })]
    public Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default)
        => service.CreateAsync(input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Book.Get" })]
    public Task<BookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => service.GetByIdAsync(id, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { "Book.Search" })]
    public Task<PagedResultDto<BookDto>> GetListAsync(
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken = default)
        => service.GetListAsync(
            new PagedRequestDto { PageIndex = pageIndex ?? 0, PageSize = pageSize ?? 0 },
            cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Put,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required,
        Permissions = new[] { "Book.Update" })]
    public Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken cancellationToken = default)
        => service.UpdateAsync(id, input, cancellationToken);

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Delete,
        Route = "{id}",
        Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { "Book.Delete" },
        RequiresConcurrencyToken = true)]
    public Task DeleteAsync(Guid id, string? expectedStamp = null, CancellationToken cancellationToken = default)
        => service.DeleteAsync(id, expectedStamp, cancellationToken);
}
