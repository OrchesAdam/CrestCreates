using System.Threading.Tasks;
using CrestCreates.Identity.Entities;

namespace CrestCreates.Identity.Services
{
    public interface IIdentityService
    {
        Task<(bool Success, string Token, string RefreshToken, User User)> LoginAsync(string email, string password);
        Task<(bool Success, User User)> RegisterAsync(string firstName, string lastName, string email, string password);
        Task<(bool Success, string Token, string RefreshToken)> RefreshTokenAsync(string refreshToken);
        Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
        Task<string> GeneratePasswordResetTokenAsync(string email);
        Task<bool> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
        Task<User> GetUserByIdAsync(string userId);
        Task<User> GetUserByEmailAsync(string email);
    }
}