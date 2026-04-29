using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Domain.Entities.Extensions;

namespace LibraryManagement.Application.Services;

[CrestService]
public class BookAppService : CrestAppServiceBase<Book,Guid, BookDto, CreateBookDto, UpdateBookDto>, IBookAppService
{
    private readonly IBookRepository _repository;

    public BookAppService(ICrestRepositoryBase<Book, Guid> repository, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IBookRepository repository2) : base(repository, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _repository = repository2;
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? null : book.ToDto();
    }

    /// <summary>
    /// 演示框架查询能力的多种使用方式：
    /// 1. FilterBuilder&lt;T&gt; — 强类型 Lambda 链式过滤构建器
    /// 2. SortBuilder&lt;T&gt; — 强类型 Lambda 链式排序构建器
    /// 3. QueryRequest&lt;T&gt; — 组合过滤+排序+分页的请求对象
    /// 4. 基类 SearchAsync — 自动执行权限检查 + 数据权限过滤 + 查询执行
    /// </summary>
    public async Task<PagedResultDto<BookDto>> SearchBooksAsync(
        string? keyword = null,
        string? author = null,
        int? minCopies = null,
        string? sortField = null,
        bool sortDescending = false,
        int pageIndex = 0,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // === 方式1: FilterBuilder<T> — 强类型 Lambda 链式过滤构建器 ===
        var filterBuilder = FilterBuilder<Book>.Create();
        if (!string.IsNullOrWhiteSpace(keyword))
            filterBuilder.Contains(b => b.Title, keyword);
        if (!string.IsNullOrWhiteSpace(author))
            filterBuilder.Equal(b => b.Author, author);
        if (minCopies.HasValue)
            filterBuilder.GreaterThanOrEqual(b => b.TotalCopies, minCopies.Value);

        // === 方式2: SortBuilder<T> — 强类型 Lambda 链式排序构建器 ===
        var sortBuilder = SortBuilder<Book>.Create();
        if (!string.IsNullOrWhiteSpace(sortField))
        {
            sortBuilder = sortField.ToLowerInvariant() == "title"
                ? (sortDescending ? sortBuilder.Desc(b => b.Title) : sortBuilder.Asc(b => b.Title))
                : sortField.ToLowerInvariant() == "author"
                    ? (sortDescending ? sortBuilder.Desc(b => b.Author) : sortBuilder.Asc(b => b.Author))
                    : (sortDescending ? sortBuilder.Desc(b => b.Id) : sortBuilder.Asc(b => b.Id));
        }
        else
        {
            sortBuilder = sortBuilder.Asc(b => b.Title);
        }

        // === 方式3: QueryRequest<T> 组合过滤+排序+分页 ===
        var request = new QueryRequest<Book>(pageIndex, pageSize, filterBuilder.Build(), sortBuilder.Build());

        // === 方式4: 基类 QueryAsync → GetListAsync — 自动执行权限检查 + 数据权限过滤 + QueryExecutor 统一执行 ===
        return await QueryAsync(request, cancellationToken);
    }

    protected override Book MapToEntity(CreateBookDto dto)
    {
        return new Book(
            Guid.NewGuid(),
            dto.Title,
            dto.Author,
            dto.ISBN,
            dto.CategoryId,
            dto.TotalCopies,
            dto.Description,
            dto.PublishDate,
            dto.Publisher,
            dto.Location
        );
    }

    protected override BookDto MapToDto(Book entity)
        => entity.ToDto();

    protected override void MapToEntity(UpdateBookDto dto, Book entity)
    {
        dto.ApplyTo(entity);
    }
}
