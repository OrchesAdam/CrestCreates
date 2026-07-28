using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class RejectProcurementRequestHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => throw new InvalidOperationException("Capability execution context is required.");

    public Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        var service = context.ServiceProvider.GetRequiredService<ProcurementApplicationService>();
        object result = service.Reject(
            (RejectProcurementRequestInput)context.Input!,
            context.TenantId ?? string.Empty,
            context.UserId ?? string.Empty);
        return Task.FromResult<object?>(result);
    }
}
