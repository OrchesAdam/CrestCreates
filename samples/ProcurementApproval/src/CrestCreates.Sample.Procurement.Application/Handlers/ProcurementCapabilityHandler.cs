using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

/// <summary>
/// Shared execution-context access for the Procurement capability handlers. The generated invoker
/// activates each handler through DI inside the pipeline, so the pipeline context is the only source
/// of tenant/user identity and invocation source — handlers never read the service provider.
/// </summary>
public abstract class ProcurementCapabilityHandler
{
    private readonly ICapabilityExecutionContextAccessor _execution;

    protected ProcurementCapabilityHandler(ICapabilityExecutionContextAccessor execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    private CapabilityExecutionContext Context => _execution.Current
        ?? throw new InvalidOperationException(
            "Procurement capability handlers require an active capability execution context.");

    protected string TenantId => Context.TenantId ?? string.Empty;

    protected string UserId => Context.UserId ?? string.Empty;

    protected void RequireHumanTaskSource()
    {
        if (Context.InvocationSource != InvocationSource.HumanTask)
        {
            throw new CapabilityFailureException(
                "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN",
                "Procurement decisions may only be applied by HumanTask completion.");
        }
    }
}
