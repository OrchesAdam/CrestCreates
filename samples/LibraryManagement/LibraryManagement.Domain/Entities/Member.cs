using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Entities.Auditing;
using CrestCreates.Domain.Shared.Entities.Auditing;
using CrestCreates.Domain.Shared.Attributes;
using LibraryManagement.Domain.Shared.Constants;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Domain.Entities;

[Entity]
public class Member : AuditedEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public MemberType Type { get; private set; }
    public DateTime RegistrationDate { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public bool IsActive { get; private set; }
    public int MaxBooksAllowed { get; private set; }
    public decimal OutstandingBalance { get; private set; }
    public ICollection<Loan> Loans { get; private set; } = new List<Loan>();

    protected Member() { }

    public Member(
        Guid id,
        string name,
        string email,
        MemberType type = MemberType.Regular,
        string? phone = null,
        string? address = null,
        DateTime? expiryDate = null)
    {
        Id = id;
        SetName(name);
        SetEmail(email);
        Type = type;
        Phone = phone;
        Address = address;
        RegistrationDate = DateTime.UtcNow;
        ExpiryDate = expiryDate;
        IsActive = true;
        MaxBooksAllowed = CalculateMaxBooksAllowed(type);
        OutstandingBalance = 0;
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));
        if (name.Length > LibraryConstants.MaxMemberNameLength)
            throw new ArgumentException($"Name cannot exceed {LibraryConstants.MaxMemberNameLength} characters", nameof(name));
        
        Name = name;
    }

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty", nameof(email));
        if (email.Length > LibraryConstants.MaxEmailLength)
            throw new ArgumentException($"Email cannot exceed {LibraryConstants.MaxEmailLength} characters", nameof(email));
        
        Email = email;
    }

    public void SetPhone(string? phone)
    {
        if (phone != null && phone.Length > LibraryConstants.MaxPhoneLength)
            throw new ArgumentException($"Phone cannot exceed {LibraryConstants.MaxPhoneLength} characters", nameof(phone));
        
        Phone = phone;
    }

    public void SetType(MemberType type)
    {
        Type = type;
        MaxBooksAllowed = CalculateMaxBooksAllowed(type);
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public void AddToBalance(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        
        OutstandingBalance += amount;
    }

    public void PayBalance(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        if (amount > OutstandingBalance)
            throw new InvalidOperationException("Payment amount exceeds outstanding balance");
        
        OutstandingBalance -= amount;
    }

    public bool CanBorrow()
    {
        return IsActive && 
               (ExpiryDate == null || ExpiryDate > DateTime.UtcNow) &&
               OutstandingBalance < LibraryConstants.MaxLateFee;
    }

    public int GetCurrentLoanCount()
    {
        return Loans.Count(l => l.Status == LoanStatus.Active);
    }

    public bool HasReachedLoanLimit()
    {
        return GetCurrentLoanCount() >= MaxBooksAllowed;
    }

    private static int CalculateMaxBooksAllowed(MemberType type)
    {
        return type switch
        {
            MemberType.Student => 3,
            MemberType.Teacher => 10,
            MemberType.Staff => 8,
            MemberType.VIP => 15,
            _ => LibraryConstants.MaxBooksPerMember
        };
    }
}
