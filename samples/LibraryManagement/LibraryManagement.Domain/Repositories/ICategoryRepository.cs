using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.Repositories;

public interface ICategoryRepository : ICrestRepositoryBase<Category, Guid>
{
    Task<Category?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetRootCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
    Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
