using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class CreateTicketDtoValidator : AbstractValidator<CreateTicketDto>
{
    public CreateTicketDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("工单标题不能为空")
            .Length(5, 200).WithMessage("工单标题长度需在5-200字符之间");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("工单描述不能为空")
            .MinimumLength(10).WithMessage("工单描述至少需要10个字符");

        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("客户不能为空");

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("无效的优先级");
    }
}
