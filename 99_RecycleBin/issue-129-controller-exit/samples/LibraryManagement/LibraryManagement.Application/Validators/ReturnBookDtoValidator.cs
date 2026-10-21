using FluentValidation;
using LibraryManagement.Application.Contracts.DTOs;

namespace LibraryManagement.Application.Validators;

public sealed class ReturnBookDtoValidator : AbstractValidator<ReturnBookDto>
{
    public ReturnBookDtoValidator()
    {
        RuleFor(x => x.LoanId).NotEmpty().WithMessage("LoanId 不能为空");
    }
}
