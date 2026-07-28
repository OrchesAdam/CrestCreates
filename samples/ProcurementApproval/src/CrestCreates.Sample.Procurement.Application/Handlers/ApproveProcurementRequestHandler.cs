using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ApproveProcurementRequestHandler : ICapabilityHandlerInvoker
{
    private readonly InMemoryProcurementRequestStore _store;

    public ApproveProcurementRequestHandler(InMemoryProcurementRequestStore store)
    {
        _store = store;
    }

    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
    {
        var dto = (ApproveProcurementRequestInput)input!;
        var request = _store.GetById(dto.RequestId);

        if (request is null)
        {
            return Task.FromResult<object?>(new ProcurementRequestResult
            {
                RequestId = dto.RequestId,
                Status = "NotFound"
            });
        }

        request.Approve(dto.ApproverId, dto.Comment);

        return Task.FromResult<object?>(new ProcurementRequestResult
        {
            Id = request.Id,
            RequestId = request.Id,
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount.Amount,
            Currency = request.Amount.Currency,
            RequesterId = request.RequesterId,
            Category = request.Category,
            Status = request.Status.ToString(),
            ApproverId = dto.ApproverId,
            ApprovedAt = DateTime.UtcNow
        });
    }
}
