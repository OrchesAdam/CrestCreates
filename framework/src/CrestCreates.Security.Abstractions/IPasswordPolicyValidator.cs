namespace CrestCreates.Security.Abstractions;

public interface IPasswordPolicyValidator
{
    void Validate(string password);
}
