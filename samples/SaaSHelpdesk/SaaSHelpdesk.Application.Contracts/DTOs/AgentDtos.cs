using System;

namespace SaaSHelpdesk.Application.Contracts.DTOs;

public class IdentityUserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? Role { get; set; }
}

public class CreateAgentDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Role { get; set; }
}
