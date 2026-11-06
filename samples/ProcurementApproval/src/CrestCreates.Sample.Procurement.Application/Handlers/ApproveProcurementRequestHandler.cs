using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.ApproveCapability)]
public sealed class ApproveProcurementRequestHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<ApproveProcurementRequestInput, ProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;
    private readonly IProcurementApprovalOrchestrator _approval;

    public ApproveProcurementRequestHandler(
        ProcurementApplicationService service,
        IProcurementApprovalOrchestrator approval,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _approval = approval ?? throw new ArgumentNullException(nameof(approval));
    }

    public async Task<ProcurementRequestResult> ExecuteAsync(
        ApproveProcurementRequestInput input, CancellationToken ct)
    {
        await _approval.CompleteDecisionAsync(input.RequestId, "Approve", input.Comment, ct)
            .ConfigureAwait(false);
        return _service.Get(new GetProcurementRequestInput { RequestId = input.RequestId }, TenantId);
    }
}
