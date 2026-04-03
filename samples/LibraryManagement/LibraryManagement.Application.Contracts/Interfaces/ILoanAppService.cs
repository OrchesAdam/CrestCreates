using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface ILoanAppService
{
    Task<LoanDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<LoanDto>> SearchAsync(LoanSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoanDto>> GetOverdueLoansAsync(CancellationToken cancellationToken = default);
    Task<LoanDto> CreateAsync(CreateLoanDto input, CancellationToken cancellationToken = default);
    Task<LoanDto> ReturnBookAsync(ReturnBookDto input, CancellationToken cancellationToken = default);
    Task<LoanDto> ExtendLoanAsync(ExtendLoanDto input, CancellationToken cancellationToken = default);
    Task ProcessOverdueLoansAsync(CancellationToken cancellationToken = default);
}
