using System;

namespace LibraryManagement.Application.Contracts.DTOs;

public class ExtendLoanDto
{
    public Guid LoanId { get; set; }
    public int AdditionalDays { get; set; }
}
