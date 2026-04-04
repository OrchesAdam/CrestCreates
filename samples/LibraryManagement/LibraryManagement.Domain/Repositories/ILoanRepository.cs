using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Repositories;

public interface ILoanRepository : ICrestRepositoryBase<Loan, Guid>
{
    Task<IReadOnlyList<Loan>> GetByMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Loan>> GetByBookAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Loan>> GetActiveLoansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Loan>> GetOverdueLoansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Loan>> GetByStatusAsync(LoanStatus status, CancellationToken cancellationToken = default);
    Task<int> GetActiveLoanCountByMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveLoanAsync(Guid memberId, Guid bookId, CancellationToken cancellationToken = default);
}
