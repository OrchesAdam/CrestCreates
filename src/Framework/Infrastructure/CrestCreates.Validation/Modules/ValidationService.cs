using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using CrestCreates.Validation.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Validation.Modules
{
    public class ValidationService : IValidationService
    {
        private readonly IServiceProvider _serviceProvider;

        public ValidationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ValidationResult Validate<T>(T instance)
        {
            var validator = _serviceProvider.GetService<IValidator<T>>();
            if (validator == null)
            {
                return ValidationResult.Success;
            }

            // 直接调用 FluentValidation 并转换为我们的 ValidationResult
            var fluentResult = validator.Validate(instance);
            return ConvertResult(fluentResult);
        }

        public async Task<ValidationResult> ValidateAsync<T>(T instance)
        {
            var validator = _serviceProvider.GetService<IValidator<T>>();
            if (validator == null)
            {
                return ValidationResult.Success;
            }

            var fluentResult = await validator.ValidateAsync(instance);
            return ConvertResult(fluentResult);
        }

        private static ValidationResult ConvertResult(FluentValidation.Results.ValidationResult fluentResult)
        {
            if (fluentResult.IsValid)
            {
                return ValidationResult.Success;
            }

            var details = fluentResult.Errors.Select(e => new ValidationErrorDetail
            {
                PropertyName = e.PropertyName,
                ErrorMessage = e.ErrorMessage,
                ErrorCode = e.ErrorCode,
                AttemptedValue = e.AttemptedValue?.ToString()
            }).ToList();

            return ValidationResult.FailureWithCodes(details);
        }
    }
}
