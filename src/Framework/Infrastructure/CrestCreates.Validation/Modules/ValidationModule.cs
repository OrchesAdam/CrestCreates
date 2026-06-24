using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;

namespace CrestCreates.Validation.Modules
{
    [CrestModule]
    public class ValidationModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            // 注册FluentValidation
            services.AddValidatorsFromAssembly(typeof(ValidationModule).Assembly);

            // 注册验证服务
            services.AddScoped<IValidationService, ValidationService>();
        }
    }
}
