using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

/// <summary>
/// The Agent creation body is validated before the business service or its unit of work is entered.
/// Authored here so the formal projection precompile can discover the closed validator at build time;
/// the default service generator skips its own duplicate when this validator exists.
/// </summary>
public sealed class CreateAgentDtoValidator : AbstractValidator<CreateAgentDto>
{
    public CreateAgentDtoValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("UserName 不能为空");
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email 不能为空");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Email 格式不正确");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password 不能为空");
    }
}
