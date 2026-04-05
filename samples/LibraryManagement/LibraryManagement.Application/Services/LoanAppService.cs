using AutoMapper;
using LibraryManagement.Application.Contracts.DTOs;
using LibraryManagement.Application.Contracts.Interfaces;
using LibraryManagement.Domain.Entities;
using LibraryManagement.Domain.Repositories;
using LibraryManagement.Domain.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;
using CrestCreates.Application.Services;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.UnitOfWork;
using CrestCreates.Infrastructure.Authorization;

namespace LibraryManagement.Application.Services;

[CrestService]
public class LoanAppService : CrestAppServiceBase<Loan, Guid, LoanDto, CreateLoanDto, LoanDto>, ILoanAppService
{
    private readonly ILoanRepository _loanRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IMemberRepository _memberRepository;
    private readonly IMapper _mapper;


    public LoanAppService(ICrestRepositoryBase<Loan, Guid> repository, IMapper mapper, IUnitOfWork unitOfWork, ICurrentUser currentUser, IDataPermissionFilter dataPermissionFilter, IPermissionChecker permissionChecker, ILoanRepository loanRepository, IBookRepository bookRepository, IMemberRepository memberRepository) : base(repository, mapper, unitOfWork, currentUser, dataPermissionFilter, permissionChecker)
    {
        _loanRepository = loanRepository;
        _bookRepository = bookRepository;
        _memberRepository = memberRepository;
        _mapper = mapper;
    }

    public override async Task<LoanDto> CreateAsync(CreateLoanDto input, CancellationToken cancellationToken = default)
    {
        // Validate member
        var member = await _memberRepository.GetAsync(input.MemberId);
        if (member == null)
            throw new Exception($"Member with id {input.MemberId} not found");
        if (!member.CanBorrow())
            throw new Exception("Member cannot borrow books");
        if (member.HasReachedLoanLimit())
            throw new Exception("Member has reached loan limit");

        // Validate book
        var book = await _bookRepository.GetAsync(input.BookId);
        if (book == null)
            throw new Exception($"Book with id {input.BookId} not found");
        if (!book.CanBorrow())
            throw new Exception("Book is not available for borrowing");

        // Check if member already has this book
        if (await _loanRepository.HasActiveLoanAsync(input.MemberId, input.BookId, cancellationToken))
            throw new Exception("Member already has this book on loan");

        // Create loan
        var loan = new Loan(
            Guid.NewGuid(),
            input.BookId,
            input.MemberId,
            input.LoanDays ?? 0,
            input.Notes
        );

        // Update book available copies
        book.UpdateAvailableCopies(-1);
        await _bookRepository.UpdateAsync(book);

        await _loanRepository.InsertAsync(loan);
        return await MapToDtoAsync(loan);
    }

    public async Task<PagedResultDto<LoanDto>> SearchAsync(LoanSearchRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Loan> loans;

        if (request.MemberId.HasValue)
        {
            loans = await _loanRepository.GetByMemberAsync(request.MemberId.Value, cancellationToken);
        }
        else if (request.BookId.HasValue)
        {
            loans = await _loanRepository.GetByBookAsync(request.BookId.Value, cancellationToken);
        }
        else if (request.Status.HasValue)
        {
            loans = await _loanRepository.GetByStatusAsync(request.Status.Value, cancellationToken);
        }
        else
        {
            loans = await _loanRepository.GetListAsync(cancellationToken);
        }

        if (request.IsOverdue.HasValue)
        {
            loans = loans.Where(l => l.IsOverdue() == request.IsOverdue.Value).ToList();
        }

        var totalCount = loans.Count;
        var pagedLoans = loans
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var dtos = new List<LoanDto>();
        foreach (var loan in pagedLoans)
        {
            dtos.Add(await MapToDtoAsync(loan));
        }

        return new PagedResultDto<LoanDto>(dtos, totalCount, request.PageIndex, request.PageSize);
    }

    public async Task<IReadOnlyList<LoanDto>> GetOverdueLoansAsync(CancellationToken cancellationToken = default)
    {
        var loans = await _loanRepository.GetOverdueLoansAsync(cancellationToken);
        var dtos = new List<LoanDto>();
        foreach (var loan in loans)
        {
            dtos.Add(await MapToDtoAsync(loan));
        }
        return dtos;
    }

    public async Task<LoanDto> ReturnBookAsync(ReturnBookDto input, CancellationToken cancellationToken = default)
    {
        var loan = await _loanRepository.GetAsync(input.LoanId);
        if (loan == null)
            throw new Exception($"Loan with id {input.LoanId} not found");

        var book = await _bookRepository.GetAsync(loan.BookId);
        if (book == null)
            throw new Exception($"Book with id {loan.BookId} not found");

        // Return the book
        loan.Return();
        await _loanRepository.UpdateAsync(loan);

        // Update book available copies
        book.UpdateAvailableCopies(1);
        await _bookRepository.UpdateAsync(book);

        // Add late fee to member balance if applicable
        if (loan.LateFee.HasValue && loan.LateFee.Value > 0)
        {
            var member = await _memberRepository.GetAsync(loan.MemberId);
            if (member != null)
            {
                member.AddToBalance(loan.LateFee.Value);
                await _memberRepository.UpdateAsync(member);
            }
        }

        return await MapToDtoAsync(loan);
    }

    public async Task<LoanDto> ExtendLoanAsync(ExtendLoanDto input, CancellationToken cancellationToken = default)
    {
        var loan = await _loanRepository.GetAsync(input.LoanId);
        if (loan == null)
            throw new Exception($"Loan with id {input.LoanId} not found");

        loan.ExtendDueDate(input.AdditionalDays);
        await _loanRepository.UpdateAsync(loan);

        return await MapToDtoAsync(loan);
    }

    public async Task ProcessOverdueLoansAsync(CancellationToken cancellationToken = default)
    {
        var activeLoans = await _loanRepository.GetActiveLoansAsync(cancellationToken);
        foreach (var loan in activeLoans)
        {
            loan.CheckOverdue();
            if (loan.Status == LoanStatus.Overdue)
            {
                await _loanRepository.UpdateAsync(loan);
            }
        }
    }

    private async Task<LoanDto> MapToDtoAsync(Loan loan)
    {
        var dto = _mapper.Map<LoanDto>(loan);
        
        var book = await _bookRepository.GetAsync(loan.BookId);
        if (book != null)
        {
            dto.BookTitle = book.Title;
            dto.BookISBN = book.ISBN;
        }

        var member = await _memberRepository.GetAsync(loan.MemberId);
        if (member != null)
        {
            dto.MemberName = member.Name;
            dto.MemberEmail = member.Email;
        }

        dto.OverdueDays = loan.GetOverdueDays();
        return dto;
    }
}
