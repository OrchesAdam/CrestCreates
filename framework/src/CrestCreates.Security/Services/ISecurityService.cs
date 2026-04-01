namespace CrestCreates.Security.Services;

public interface ISecurityService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
    string GenerateRandomToken(int length = 32);
    bool ValidateToken(string token, string expectedToken);
}