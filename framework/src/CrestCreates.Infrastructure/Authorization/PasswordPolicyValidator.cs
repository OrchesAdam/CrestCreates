using System;
using System.Linq;
using CrestCreates.Domain.Authorization;
using Microsoft.Extensions.Options;

namespace CrestCreates.Infrastructure.Authorization;

public class PasswordPolicyValidator : IPasswordPolicyValidator
{
    private readonly IdentityAuthenticationOptions _options;

    public PasswordPolicyValidator(IOptions<IdentityAuthenticationOptions> options)
    {
        _options = options.Value;
    }

    public void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("密码不能为空", nameof(password));
        }

        if (password.Length < _options.MinPasswordLength)
        {
            throw new ArgumentException($"密码长度不能少于 {_options.MinPasswordLength} 位", nameof(password));
        }

        if (_options.RequireDigit && !password.Any(char.IsDigit))
        {
            throw new ArgumentException("密码必须包含数字", nameof(password));
        }

        if (_options.RequireLowercase && !password.Any(char.IsLower))
        {
            throw new ArgumentException("密码必须包含小写字母", nameof(password));
        }

        if (_options.RequireUppercase && !password.Any(char.IsUpper))
        {
            throw new ArgumentException("密码必须包含大写字母", nameof(password));
        }

        if (_options.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            throw new ArgumentException("密码必须包含特殊字符", nameof(password));
        }
    }
}
