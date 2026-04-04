using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface IBookAppService
{
    Task<BookDto> CreateAsync(CreateBookDto input, CancellationToken cancellationToken = default);
    Task<BookDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookDto> UpdateAsync(Guid id, UpdateBookDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookDto?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
}