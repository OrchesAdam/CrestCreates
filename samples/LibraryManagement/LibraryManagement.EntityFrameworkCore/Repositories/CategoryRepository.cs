using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.EFCore.Repositories;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.EntityFrameworkCore.Repositories;

public class CategoryRepository : EfCoreRepository<Category, Guid>, ICategoryRepository
{
    public CategoryRepository(IDataBaseContext dbContext) : base(dbContext)
    {
    }

    public async Task<Category?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable().Where(c => c.ParentId == null).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().Where(c => c.ParentId == parentId).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable().AnyAsync(c => c.ParentId == categoryId, cancellationToken);
    }
}
