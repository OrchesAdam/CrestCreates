using System.ComponentModel.DataAnnotations;

namespace CrestCreates.Application.Contracts.DTOs.Auth;

public class LoginDto
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string? TenantId { get; set; }
}
