using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.Attributes;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;

namespace LibraryManagement.Application.Contracts.Interfaces;

[CrestCrudApiController("BookCrudDemo", "api/demo/books")]
public interface IBookAppService : ICrudAppService<Guid, BookDto, CreateBookDto, UpdateBookDto, PagedRequestDto>
{
    Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
}
