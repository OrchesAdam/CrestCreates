namespace CrestCreates.Application.Contracts.DTOs.Auth;

public class LoginResultDto
{
    public TokenDto Token { get; set; } = new();
    public UserInfoDto User { get; set; } = new();
}
