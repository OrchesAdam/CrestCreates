namespace CrestCreates.Security.Abstractions;

public interface ITokenGenerator
{
    string GenerateRandomToken(int length = 32);
    bool ValidateToken(string token, string expectedToken);
}
