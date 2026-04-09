namespace CrestCreates.Application.Contracts.DTOs.Identity;

public class ChangeIdentityPasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
