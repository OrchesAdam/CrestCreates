namespace CrestCreates.AuditLogging.Abstractions.MethodAccountability;

public interface IAuditedMethodAccountabilityRuntime
{
    IAuditedMethodInvocationState Enter(
        AuditedMethodInvocationDescriptor descriptor);

    void SetOutcome(
        IAuditedMethodInvocationState state,
        AuditedMethodInvocationOutcome outcome);

    ValueTask ExitAsync(IAuditedMethodInvocationState state);
}
