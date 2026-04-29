using LibraryManagement.Application.Contracts.DTOs;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface IBookAppService : ICrudAppService<Guid, BookDto, CreateBookDto, UpdateBookDto, PagedRequestDto>
{
    Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用 FilterDescriptor + SortDescriptor 构建的动态查询
    /// </summary>
    Task<PagedResultDto<BookDto>> SearchBooksAsync(
        string? keyword = null,
        string? author = null,
        int? minCopies = null,
        string? sortField = null,
        bool sortDescending = false,
        int pageIndex = 0,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
}
