using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using CrestCreates.Modularity;
using CrestCreates.Validation.Validators;

namespace CrestCreates.Validation.Modules
{
    public class ValidationModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);

            // 注册FluentValidation
            services.AddValidatorsFromAssembly(typeof(ValidationModule).Assembly);

            // 注册验证服务
            services.AddScoped<IValidationService, ValidationService>();
        }
    }

    public interface IValidationService
    {
        ValidationResult Validate<T>(T instance);
        Task<ValidationResult> ValidateAsync<T>(T instance);
    }

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

            var result = validator.Validate(instance);
            if (result.IsValid)
            {
                return ValidationResult.Success;
            }

            var errors = result.Errors.Select(e => e.ErrorMessage).ToList();
            return ValidationResult.Failure(errors.ToArray());
        }

        public async Task<ValidationResult> ValidateAsync<T>(T instance)
        {
            var validator = _serviceProvider.GetService<IValidator<T>>();
            if (validator == null)
            {
                return ValidationResult.Success;
            }

            var result = await validator.ValidateAsync(instance);
            if (result.IsValid)
            {
                return ValidationResult.Success;
            }

            var errors = result.Errors.Select(e => e.ErrorMessage).ToList();
            return ValidationResult.Failure(errors.ToArray());
        }
    }
}