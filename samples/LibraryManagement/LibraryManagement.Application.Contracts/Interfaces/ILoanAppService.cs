using LibraryManagement.Application.Contracts.DTOs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;

namespace LibraryManagement.Application.Contracts.Interfaces;

public interface ILoanAppService  : ICrestAppServiceBase<Loan, Guid, LoanDto, CreateLoanDto, LoanDto>
{
    Task<PagedResultDto<LoanDto>> SearchAsync(LoanSearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoanDto>> GetOverdueLoansAsync(CancellationToken cancellationToken = default);
    Task<LoanDto> ReturnBookAsync(ReturnBookDto input, CancellationToken cancellationToken = default);
    Task<LoanDto> ExtendLoanAsync(ExtendLoanDto input, CancellationToken cancellationToken = default);
    Task ProcessOverdueLoansAsync(CancellationToken cancellationToken = default);
}
