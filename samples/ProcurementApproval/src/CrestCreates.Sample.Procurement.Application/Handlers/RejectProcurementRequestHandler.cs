using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.RejectCapability)]
public sealed class RejectProcurementRequestHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<RejectProcurementRequestInput, ProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;
    private readonly IProcurementApprovalOrchestrator _approval;

    public RejectProcurementRequestHandler(
        ProcurementApplicationService service,
        IProcurementApprovalOrchestrator approval,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _approval = approval ?? throw new ArgumentNullException(nameof(approval));
    }

    public async Task<ProcurementRequestResult> ExecuteAsync(
        RejectProcurementRequestInput input, CancellationToken ct)
    {
        await _approval.CompleteDecisionAsync(input.RequestId, "Reject", input.Reason, ct)
            .ConfigureAwait(false);
        return _service.Get(new GetProcurementRequestInput { RequestId = input.RequestId }, TenantId);
    }
}
