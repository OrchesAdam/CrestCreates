using FluentValidation;
using LibraryManagement.Application.Contracts.DTOs;

namespace LibraryManagement.Application.Validators;

public sealed class ExtendLoanDtoValidator : AbstractValidator<ExtendLoanDto>
{
    public ExtendLoanDtoValidator()
    {
        RuleFor(x => x.LoanId).NotEmpty().WithMessage("LoanId 不能为空");
        RuleFor(x => x.AdditionalDays).GreaterThan(0).WithMessage("AdditionalDays 必须大于 0");
    }
}
