using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Domain.Entities;
using CrestCreates.Sample.Procurement.Domain.ValueObjects;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class SubmitProcurementRequestHandler : ICapabilityHandlerInvoker
{
    public async Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var dto = (SubmitProcurementRequestInput)input!;
        var money = new Money(dto.Amount, dto.Currency);
        var request = new ProcurementRequest(
            Guid.NewGuid(),
            dto.Title,
            dto.Description,
            money,
            dto.RequesterId,
            dto.Category);

        return new SubmitProcurementRequestResult
        {
            RequestId = request.Id,
            Status = request.Status.ToString(),
            Amount = request.Amount.Amount,
            Currency = request.Amount.Currency,
            RequiresApproval = request.RequiresApproval
        };
    }
}
