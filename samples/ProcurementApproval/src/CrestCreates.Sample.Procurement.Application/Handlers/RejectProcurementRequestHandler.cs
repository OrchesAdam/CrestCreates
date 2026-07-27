using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class RejectProcurementRequestHandler : ICapabilityHandlerInvoker
{
    public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var dto = (RejectProcurementRequestInput)input!;
        return new ProcurementRequestResult
        {
            Id = dto.RequestId,
            Title = "Rejected Request",
            Amount = 0m,
            Currency = "USD",
            RequesterId = string.Empty,
            Category = string.Empty,
            Status = "Rejected",
            ApproverId = dto.ApproverId,
            RejectedAt = DateTime.UtcNow
        };
    }
}
