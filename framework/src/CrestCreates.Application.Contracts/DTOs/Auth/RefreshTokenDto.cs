using System.ComponentModel.DataAnnotations;

namespace CrestCreates.Application.Contracts.DTOs.Auth;

public class RefreshTokenDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
