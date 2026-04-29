using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Query;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;

namespace LibraryManagement.Application.Services;

[CrestService]
public class BookAppService : CrestAppServiceBase<Book,Guid, BookDto, CreateBookDto, UpdateBookDto>, IBookAppService
{
    private readonly IBookRepository _repository;
    private readonly IMapper _mapper;


    public BookAppService(ICrestRepositoryBase<Book, Guid> repository, IMapper mapper, IServiceProvider serviceProvider, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, IBookRepository repository2) : base(repository, mapper, serviceProvider, currentUser, dataPermissionFilter, permissionChecker)
    {
        _repository = repository2;
        _mapper = mapper;
    }

    public async Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var book = await _repository.GetByIsbnAsync(isbn, cancellationToken);
        return book == null ? null : _mapper.Map<BookDto>(book);
    }

    /// <summary>
    /// 演示框架查询能力的多种使用方式：
    /// 1. FilterBuilder&lt;T&gt; — 强类型 Lambda 链式过滤构建器
    /// 2. SortBuilder&lt;T&gt; — 强类型 Lambda 链式排序构建器
    /// 3. FilterDescriptor / SortDescriptor — 通用描述符
    /// 4. QueryRequest&lt;T&gt; — 组合过滤+排序+分页的请求对象
    /// 5. QueryExecutor&lt;T&gt; — 执行查询的静态工具类
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

        // === 方式4: QueryExecutor<T> 执行查询 ===
        var query = _repository.GetQueryable();
        query = QueryExecutor<Book>.Execute(query, request);

        var totalCount = query.Count();
        var items = query.ToList();
        var dtos = _mapper.Map<List<BookDto>>(items);

        return new PagedResultDto<BookDto>(dtos, totalCount, pageIndex, pageSize);
    }

    protected override void MapToEntity(UpdateBookDto dto, Book entity)
    {
        entity.SetTitle(dto.Title);
        entity.SetAuthor(dto.Author);
        entity.SetISBN(dto.ISBN);
        entity.SetParent(dto.CategoryId);
        entity.SetStatus((LibraryManagement.Domain.Shared.Enums.BookStatus)dto.Status);
        entity.SetDescription(dto.Description);

        // 注意：TotalCopies 和 AvailableCopies 是只读属性，需要通过其他方法修改
        // 这里我们只更新可以直接设置的属性
        // entity.TotalCopies = dto.TotalCopies; // 只读
        // entity.AvailableCopies = dto.AvailableCopies; // 只读
        // entity.Location = dto.Location; // 只读
        // entity.Publisher = dto.Publisher; // 只读
        // entity.PublishDate = dto.PublishDate; // 只读
    }
}
