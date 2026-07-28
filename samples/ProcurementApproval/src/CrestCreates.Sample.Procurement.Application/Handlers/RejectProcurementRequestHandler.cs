using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class RejectProcurementRequestHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException("Capability execution context is required.");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var input = (RejectProcurementRequestInput)context.Input!;
        var approval = context.ServiceProvider.GetRequiredService<IProcurementApprovalOrchestrator>();
        await approval.CompleteDecisionAsync(
            input.RequestId,
            "Reject",
            input.Reason,
            ct).ConfigureAwait(false);
        var service = context.ServiceProvider.GetRequiredService<ProcurementApplicationService>();
        return service.Get(
            new GetProcurementRequestInput { RequestId = input.RequestId },
            context.TenantId ?? string.Empty);
    }
}
