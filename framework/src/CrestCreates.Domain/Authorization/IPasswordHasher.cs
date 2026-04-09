namespace CrestCreates.Domain.Authorization;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string hashedPassword, string providedPassword);
}

public interface IPasswordPolicyValidator
{
    void Validate(string password);
}
