using System;
using System.Text;
using CrestCreates.Domain.Settings;

namespace CrestCreates.Infrastructure.Settings;

public class SettingEncryptionService : ISettingEncryptionService
{
    private const string MaskedValue = "***";

    public string Protect(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return protectedValue;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
        catch
        {
            return protectedValue;
        }
    }

    public string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 8)
        {
            return MaskedValue;
        }

        return value[..4] + "****" + value[^4..];
    }
}
