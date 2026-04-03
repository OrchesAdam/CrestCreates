using System;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Application.Contracts.DTOs;

public class LoanDto
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BookISBN { get; set; } = string.Empty;
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberEmail { get; set; } = string.Empty;
    public DateTime LoanDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public LoanStatus Status { get; set; }
    public decimal? LateFee { get; set; }
    public int OverdueDays { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateLoanDto
{
    public Guid MemberId { get; set; }
    public Guid BookId { get; set; }
    public int? LoanDays { get; set; }
    public string? Notes { get; set; }
}

public class ReturnBookDto
{
    public Guid LoanId { get; set; }
}

public class ExtendLoanDto
{
    public Guid LoanId { get; set; }
    public int AdditionalDays { get; set; }
}

public class LoanSearchRequest
{
    public Guid? MemberId { get; set; }
    public Guid? BookId { get; set; }
    public LoanStatus? Status { get; set; }
    public bool? IsOverdue { get; set; }
    public int PageIndex { get; set; } = 0;
    public int PageSize { get; set; } = 10;
}
