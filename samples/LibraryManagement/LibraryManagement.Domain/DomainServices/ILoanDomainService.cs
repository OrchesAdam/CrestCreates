using System;
using System.Threading;
using System.Threading.Tasks;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Domain.DomainServices;

public interface ILoanDomainService
{
    Task<Loan> CreateLoanAsync(Guid memberId, Guid bookId, int? loanDays = null, CancellationToken cancellationToken = default);
    Task ReturnBookAsync(Guid loanId, CancellationToken cancellationToken = default);
    Task ExtendLoanAsync(Guid loanId, int additionalDays, CancellationToken cancellationToken = default);
    Task MarkBookAsLostAsync(Guid loanId, CancellationToken cancellationToken = default);
    Task ProcessOverdueLoansAsync(CancellationToken cancellationToken = default);
}
