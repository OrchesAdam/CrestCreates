using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Validation.Validators;

namespace CrestCreates.Validation.Modules
{
    public interface IValidationService
    {
        ValidationResult Validate<T>(T instance);
        Task<ValidationResult> ValidateAsync<T>(T instance);

        /// <summary>
        /// Additive cancellation-aware overload. The legacy overloads delegate here with
        /// <see cref="CancellationToken.None"/> so there is one validation execution path.
        /// </summary>
        Task<ValidationResult> ValidateAsync<T>(T instance, CancellationToken cancellationToken);
    }
}
