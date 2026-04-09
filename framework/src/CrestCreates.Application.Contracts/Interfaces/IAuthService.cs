using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Auth;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IAuthService
{
    Task<LoginResultDto> LoginAsync(LoginDto input);
    Task<TokenDto> RefreshTokenAsync(RefreshTokenDto input);
    Task<UserInfoDto> GetCurrentUserAsync();
    Task<bool> ValidateTokenAsync(string token);
    Task RevokeTokenAsync(string userId);
}
