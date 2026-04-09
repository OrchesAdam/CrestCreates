using System;
using CrestCreates.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.Infrastructure.Tests.Identity;

public class PasswordPolicyValidatorTests
{
    [Fact]
    public void Validate_WithMissingDigit_ThrowsArgumentException()
    {
        var validator = new PasswordPolicyValidator(Options.Create(new IdentityAuthenticationOptions()));

        var action = () => validator.Validate("Password");

        action.Should().Throw<ArgumentException>()
            .WithMessage("*包含数字*");
    }

    [Fact]
    public void Validate_WithStrongPassword_DoesNotThrow()
    {
        var validator = new PasswordPolicyValidator(Options.Create(new IdentityAuthenticationOptions()));

        var action = () => validator.Validate("Password1");

        action.Should().NotThrow();
    }
}
