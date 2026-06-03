using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class UpdateTicketDtoValidator : AbstractValidator<UpdateTicketDto>
{
    public UpdateTicketDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("工单标题不能为空")
            .Length(5, 200).WithMessage("工单标题长度需在5-200字符之间");
    }
}
