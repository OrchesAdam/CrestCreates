using System.Threading.Tasks;
using CrestCreates.Validation.Validators;

namespace CrestCreates.Validation.Modules
{
    public interface IValidationService
    {
        ValidationResult Validate<T>(T instance);
        Task<ValidationResult> ValidateAsync<T>(T instance);
    }
}
