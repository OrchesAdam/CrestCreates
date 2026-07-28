using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ApplyApprovalDecisionHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException("Capability execution context is required.");

    public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        RequireHumanTaskSource(context);
        var service = context.ServiceProvider.GetRequiredService<ProcurementApplicationService>();
        object result = service.ApplyApprovalDecision(
            (ApproveProcurementRequestInput)context.Input!,
            context.TenantId ?? string.Empty,
            context.UserId ?? string.Empty);
        return Task.FromResult<object?>(result);
    }

    internal static void RequireHumanTaskSource(CapabilityExecutionContext context)
    {
        if (context.InvocationSource != InvocationSource.HumanTask)
        {
            throw new CapabilityFailureException(
                "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN",
                "Procurement decisions may only be applied by HumanTask completion.");
        }
    }
}
