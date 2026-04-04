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

namespace LibraryManagement.Application.Services;

public class LoanAppService : ILoanAppService
{
    private readonly ILoanRepository _loanRepository;
    private readonly IBookRepository _bookRepository;
    private readonly IMemberRepository _memberRepository;
    private readonly IMapper _mapper;

    public LoanAppService(
        ILoanRepository loanRepository,
        IBookRepository bookRepository,
        IMemberRepository memberRepository,
        IMapper mapper)
    {
        _loanRepository = loanRepository;
        _bookRepository = bookRepository;
        _memberRepository = memberRepository;
        _mapper = mapper;
    }

    public async Task<LoanDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var loan = await _loanRepository.GetByIdAsync(id);
        if (loan == null)
            throw new Exception($"Loan with id {id} not found");

        return await MapToDtoAsync(loan);
    }

    public async Task<PagedResult<LoanDto>> SearchAsync(LoanSearchRequest request, CancellationToken cancellationToken = default)
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
            loans = await _loanRepository.GetAllAsync();
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

        return new PagedResult<LoanDto>(dtos, totalCount, request.PageIndex, request.PageSize);
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

    public async Task<LoanDto> CreateAsync(CreateLoanDto input, CancellationToken cancellationToken = default)
    {
        // Validate member
        var member = await _memberRepository.GetByIdAsync(input.MemberId);
        if (member == null)
            throw new Exception($"Member with id {input.MemberId} not found");
        if (!member.CanBorrow())
            throw new Exception("Member cannot borrow books");
        if (member.HasReachedLoanLimit())
            throw new Exception("Member has reached loan limit");

        // Validate book
        var book = await _bookRepository.GetByIdAsync(input.BookId);
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

        await _loanRepository.AddAsync(loan);
        return await MapToDtoAsync(loan);
    }

    public async Task<LoanDto> ReturnBookAsync(ReturnBookDto input, CancellationToken cancellationToken = default)
    {
        var loan = await _loanRepository.GetByIdAsync(input.LoanId);
        if (loan == null)
            throw new Exception($"Loan with id {input.LoanId} not found");

        var book = await _bookRepository.GetByIdAsync(loan.BookId);
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
            var member = await _memberRepository.GetByIdAsync(loan.MemberId);
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
        var loan = await _loanRepository.GetByIdAsync(input.LoanId);
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
        
        var book = await _bookRepository.GetByIdAsync(loan.BookId);
        if (book != null)
        {
            dto.BookTitle = book.Title;
            dto.BookISBN = book.ISBN;
        }

        var member = await _memberRepository.GetByIdAsync(loan.MemberId);
        if (member != null)
        {
            dto.MemberName = member.Name;
            dto.MemberEmail = member.Email;
        }

        dto.OverdueDays = loan.GetOverdueDays();
        return dto;
    }
}
