using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class CreateCategoryDtoValidator : AbstractValidator<CreateCategoryDto>
{
    public CreateCategoryDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("分类名称不能为空")
            .Length(1, 50).WithMessage("分类名称长度需在1-50字符之间");
    }
}
