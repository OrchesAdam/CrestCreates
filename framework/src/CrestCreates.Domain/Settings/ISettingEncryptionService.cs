namespace CrestCreates.Domain.Settings;

public interface ISettingEncryptionService
{
    string Protect(string value);

    string? Unprotect(string? protectedValue);

    string Mask(string? value);
}
