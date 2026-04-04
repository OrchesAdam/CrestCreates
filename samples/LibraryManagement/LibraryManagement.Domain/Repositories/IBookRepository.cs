using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.Repositories;

public interface IBookRepository : IRepository<Book, Guid>
{
    Task<Book?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> GetByAuthorAsync(string author, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
    Task<bool> IsIsbnUniqueAsync(string isbn, Guid? excludeId = null, CancellationToken cancellationToken = default);
}