using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using LibraryManagement.Domain.Shared.Constants;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Entities;

public class Loan : AuditedEntity<Guid>
{
    public Guid BookId { get; private set; }
    public Book Book { get; private set; } = null!;
    public Guid MemberId { get; private set; }
    public Member Member { get; private set; } = null!;
    public DateTime LoanDate { get; private set; }
    public DateTime DueDate { get; private set; }
    public DateTime? ReturnDate { get; private set; }
    public LoanStatus Status { get; private set; }
    public decimal? LateFee { get; private set; }
    public string? Notes { get; private set; }

    protected Loan() { }

    public Loan(
        Guid id,
        Guid bookId,
        Guid memberId,
        int loanDays = 0,
        string? notes = null)
    {
        Id = id;
        BookId = bookId;
        MemberId = memberId;
        LoanDate = DateTime.UtcNow;
        DueDate = LoanDate.AddDays(loanDays > 0 ? loanDays : LibraryConstants.DefaultLoanDays);
        Status = LoanStatus.Active;
        Notes = notes;
    }

    public void Return()
    {
        if (Status != LoanStatus.Active)
            throw new InvalidOperationException("Only active loans can be returned");

        ReturnDate = DateTime.UtcNow;
        CalculateLateFee();
        Status = LoanStatus.Returned;
    }

    public void MarkAsLost()
    {
        if (Status != LoanStatus.Active)
            throw new InvalidOperationException("Only active loans can be marked as lost");

        Status = LoanStatus.Lost;
    }

    public void CheckOverdue()
    {
        if (Status == LoanStatus.Active && DateTime.UtcNow > DueDate)
        {
            Status = LoanStatus.Overdue;
        }
    }

    public void ExtendDueDate(int additionalDays)
    {
        if (Status != LoanStatus.Active)
            throw new InvalidOperationException("Only active loans can be extended");
        
        var totalDays = (DueDate - LoanDate).Days + additionalDays;
        if (totalDays > LibraryConstants.MaxLoanDays)
            throw new InvalidOperationException($"Loan cannot exceed {LibraryConstants.MaxLoanDays} days");

        DueDate = DueDate.AddDays(additionalDays);
    }

    public void CalculateLateFee()
    {
        if (ReturnDate == null || ReturnDate <= DueDate)
        {
            LateFee = null;
            return;
        }

        var overdueDays = (ReturnDate.Value - DueDate).Days;
        var fee = overdueDays * LibraryConstants.DefaultLateFeePerDay;
        LateFee = Math.Min(fee, LibraryConstants.MaxLateFee);
    }

    public int GetOverdueDays()
    {
        if (DueDate >= DateTime.UtcNow)
            return 0;

        var endDate = ReturnDate ?? DateTime.UtcNow;
        return (endDate - DueDate).Days;
    }

    public bool IsOverdue()
    {
        return Status == LoanStatus.Overdue || 
               (Status == LoanStatus.Active && DateTime.UtcNow > DueDate);
    }
}
