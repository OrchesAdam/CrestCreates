using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.EntityFrameworkCore.Repositories;

public class BookRepository : EfCoreRepository<Book, Guid>, IBookRepository
{
    public BookRepository(LibraryDbContext context) : base(context)
    {
    }

    public async Task<Book?> GetByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(b => b.ISBN == isbn, cancellationToken);
    }

    public async Task<IReadOnlyList<Book>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(b => b.CategoryId == categoryId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Book>> GetByAuthorAsync(string author, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(b => b.Author.Contains(author)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(b => 
            b.Title.Contains(keyword) || 
            b.Author.Contains(keyword) ||
            b.ISBN.Contains(keyword))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsIsbnUniqueAsync(string isbn, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(b => b.ISBN == isbn);
        if (excludeId.HasValue)
        {
            query = query.Where(b => b.Id != excludeId.Value);
        }
        return !await query.AnyAsync(cancellationToken);
    }
}
