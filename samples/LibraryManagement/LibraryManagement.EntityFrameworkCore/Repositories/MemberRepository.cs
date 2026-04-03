using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.EntityFrameworkCore.Repositories;

public class MemberRepository : EfCoreRepository<Member, Guid>, IMemberRepository
{
    public MemberRepository(LibraryDbContext context) : base(context)
    {
    }

    public async Task<Member?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(m => m.Email == email, cancellationToken);
    }

    public async Task<IReadOnlyList<Member>> GetActiveMembersAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(m => m.IsActive).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Member>> GetMembersWithOverdueBalanceAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(m => m.OutstandingBalance > 0).ToListAsync(cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet.Where(m => m.Email == email);
        if (excludeId.HasValue)
        {
            query = query.Where(m => m.Id != excludeId.Value);
        }
        return !await query.AnyAsync(cancellationToken);
    }
}
