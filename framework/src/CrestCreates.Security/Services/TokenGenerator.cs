using System;
using System.Security.Cryptography;
using CrestCreates.Security.Abstractions;

namespace CrestCreates.Security.Services;

internal class TokenGenerator : ITokenGenerator
{
    public string GenerateRandomToken(int length = 32)
    {
        var bytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Uses ordinal comparison — tokens are not cryptographic secrets (unlike passwords),
    /// so constant-time comparison is not required here.
    /// </summary>
    public bool ValidateToken(string token, string expectedToken)
    {
        return string.Equals(token, expectedToken, StringComparison.Ordinal);
    }
}
