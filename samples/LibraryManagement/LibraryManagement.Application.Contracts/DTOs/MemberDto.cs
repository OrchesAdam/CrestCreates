using System;
using LibraryManagement.Domain.Shared.Enums;

namespace LibraryManagement.Application.Contracts.DTOs;

public class MemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public MemberType Type { get; set; }
    public DateTime RegistrationDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; }
    public int MaxBooksAllowed { get; set; }
    public int CurrentLoanCount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateMemberDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public MemberType Type { get; set; } = MemberType.Regular;
    public DateTime? ExpiryDate { get; set; }
}

public class UpdateMemberDto
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public MemberType Type { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; }
}
