using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class CreateSLAPolicyDtoValidator : AbstractValidator<CreateSLAPolicyDto>
{
    public CreateSLAPolicyDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("SLA策略名称不能为空");
        RuleFor(x => x.LowPriorityResponseMinutes).GreaterThan(0);
        RuleFor(x => x.UrgentPriorityResolutionMinutes).GreaterThan(0);
    }
}
