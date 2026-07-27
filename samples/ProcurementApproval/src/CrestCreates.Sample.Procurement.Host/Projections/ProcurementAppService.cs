using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.Projections;

[CrestService]
[CapabilityCompatibilityProjection(RoutePrefix = "api/procurement")]
public class ProcurementAppService
{
    public Task<SubmitProcurementRequestResult> SubmitAsync(SubmitProcurementRequestInput input)
    {
        var result = new SubmitProcurementRequestResult
        {
            RequestId = Guid.NewGuid(),
            Status = input.Amount > 10000m ? "PendingApproval" : "Approved",
            Amount = input.Amount,
            Currency = input.Currency,
            RequiresApproval = input.Amount > 10000m
        };
        return Task.FromResult(result);
    }

    public Task<ProcurementRequestResult> ApproveAsync(ApproveProcurementRequestInput input)
    {
        var result = new ProcurementRequestResult
        {
            RequestId = input.RequestId,
            Status = "Approved",
            ApproverId = input.ApproverId,
            ApprovedAt = DateTime.UtcNow
        };
        return Task.FromResult(result);
    }

    public Task<ProcurementRequestResult> RejectAsync(RejectProcurementRequestInput input)
    {
        var result = new ProcurementRequestResult
        {
            RequestId = input.RequestId,
            Status = "Rejected",
            ApproverId = input.ApproverId,
            RejectedAt = DateTime.UtcNow
        };
        return Task.FromResult(result);
    }
}
