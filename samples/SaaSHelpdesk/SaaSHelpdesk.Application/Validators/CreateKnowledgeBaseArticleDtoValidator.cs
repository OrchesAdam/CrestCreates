using FluentValidation;
using SaaSHelpdesk.Application.Contracts.DTOs;

namespace SaaSHelpdesk.Application.Validators;

public class CreateKnowledgeBaseArticleDtoValidator : AbstractValidator<CreateKnowledgeBaseArticleDto>
{
    public CreateKnowledgeBaseArticleDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("文章标题不能为空")
            .Length(5, 200).WithMessage("文章标题长度需在5-200字符之间");
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("文章内容不能为空")
            .MinimumLength(20).WithMessage("文章内容至少需要20个字符");
    }
}
