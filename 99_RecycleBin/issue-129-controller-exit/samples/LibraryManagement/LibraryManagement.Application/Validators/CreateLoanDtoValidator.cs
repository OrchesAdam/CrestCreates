using FluentValidation;
using LibraryManagement.Application.Contracts.DTOs;

namespace LibraryManagement.Application.Validators;

/// <summary>
/// Authored so the formal projection precompile can discover the closed validator at build time;
/// the default service generator skips its duplicate when this validator exists.
/// </summary>
public sealed class CreateLoanDtoValidator : AbstractValidator<CreateLoanDto>
{
    public CreateLoanDtoValidator()
    {
        RuleFor(x => x.BookId).NotEmpty().WithMessage("BookId 不能为空");
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("MemberId 不能为空");
        RuleFor(x => x.LoanDate).NotEmpty().WithMessage("LoanDate 不能为空");
        RuleFor(x => x.LoanDate).LessThanOrEqualTo(DateTime.Now).WithMessage("LoanDate 不能超过当前时间");
        RuleFor(x => x.DueDate).NotEmpty().WithMessage("DueDate 不能为空");
        RuleFor(x => x.DueDate).LessThanOrEqualTo(DateTime.Now).WithMessage("DueDate 不能超过当前时间");
    }
}
