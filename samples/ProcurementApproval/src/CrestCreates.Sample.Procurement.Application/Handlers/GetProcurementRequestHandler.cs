using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class GetProcurementRequestHandler : ICapabilityHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var result = new ProcurementRequestResult
        {
            RequestId = Guid.NewGuid(),
            Status = "NotFound"
        };
        return Task.FromResult<object?>(result);
    }
}
