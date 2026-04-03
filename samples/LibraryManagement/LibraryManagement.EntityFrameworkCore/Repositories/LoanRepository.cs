using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.EntityFrameworkCore.Repositories;

public class LoanRepository : EfCoreRepository<Loan, Guid>, ILoanRepository
{
    public LoanRepository(LibraryDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<Loan>> GetByMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(l => l.MemberId == memberId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> GetByBookAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(l => l.BookId == bookId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> GetActiveLoansAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(l => l.Status == LoanStatus.Active).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> GetOverdueLoansAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(l => l.Status == LoanStatus.Overdue || 
            (l.Status == LoanStatus.Active && l.DueDate < DateTime.UtcNow))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> GetByStatusAsync(LoanStatus status, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(l => l.Status == status).ToListAsync(cancellationToken);
    }

    public async Task<int> GetActiveLoanCountByMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        return await DbSet.CountAsync(l => l.MemberId == memberId && l.Status == LoanStatus.Active, cancellationToken);
    }

    public async Task<bool> HasActiveLoanAsync(Guid memberId, Guid bookId, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(l => l.MemberId == memberId && l.BookId == bookId && l.Status == LoanStatus.Active, cancellationToken);
    }
}
