using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ApplyRejectionDecisionHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException("Capability execution context is required.");

    public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        ApplyApprovalDecisionHandler.RequireHumanTaskSource(context);
        var service = context.ServiceProvider.GetRequiredService<ProcurementApplicationService>();
        object result = service.ApplyRejectionDecision(
            (RejectProcurementRequestInput)context.Input!,
            context.TenantId ?? string.Empty,
            context.UserId ?? string.Empty);
        return Task.FromResult<object?>(result);
    }
}
