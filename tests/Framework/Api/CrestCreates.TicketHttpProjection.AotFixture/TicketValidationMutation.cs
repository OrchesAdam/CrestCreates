using CrestCreates.Validation.Modules;
using CrestCreates.Validation.Validators;

namespace CrestCreates.TicketHttpProjection.AotFixture;

/// <summary>
/// Fault injection for the gate's own discriminating-power test: when
/// CREST_TICKET_GATE_MUTATION=late-validation, the invoker's pre-service validation is disabled so
/// the real TicketAppService/UoW/repository are reached with the invalid body. The gate's business/
/// UoW/write counters must then fail invalid-body-zero-business; a gate that only checks the final
/// 400 and row count would stay green.
/// </summary>
public sealed class SkippingValidationService : IValidationService
{
    public ValidationResult Validate<T>(T instance) => ValidationResult.Success;

    public Task<ValidationResult> ValidateAsync<T>(T instance)
        => Task.FromResult(ValidationResult.Success);

    public Task<ValidationResult> ValidateAsync<T>(T instance, CancellationToken cancellationToken)
        => Task.FromResult(ValidationResult.Success);
}
