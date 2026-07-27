using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ApproveProcurementRequestHandler : ICapabilityHandlerInvoker
{
    public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var dto = (ApproveProcurementRequestInput)input!;
        return new ProcurementRequestResult
        {
            Id = dto.RequestId,
            Title = "Approved Request",
            Amount = 0m,
            Currency = "USD",
            RequesterId = string.Empty,
            Category = string.Empty,
            Status = "Approved",
            ApproverId = dto.ApproverId,
            ApprovedAt = DateTime.UtcNow
        };
    }
}
