using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace CrestCreates.Security.Services;

public class SecurityService : ISecurityService
{
    private readonly PasswordHasher<object> _passwordHasher;

    public SecurityService()
    {
        _passwordHasher = new PasswordHasher<object>();
    }

    public string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(null, password);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        var result = _passwordHasher.VerifyHashedPassword(null, hashedPassword, password);
        return result == PasswordVerificationResult.Success;
    }

    public string GenerateRandomToken(int length = 32)
    {
        var bytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public bool ValidateToken(string token, string expectedToken)
    {
        return string.Equals(token, expectedToken, StringComparison.Ordinal);
    }
}