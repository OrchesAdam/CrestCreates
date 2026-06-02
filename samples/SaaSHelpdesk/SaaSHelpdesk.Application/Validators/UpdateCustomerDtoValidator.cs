using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class UpdateCustomerDtoValidator : AbstractValidator<UpdateCustomerDto>
{
    public UpdateCustomerDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("客户名称不能为空")
            .Length(1, 100);
    }
}
