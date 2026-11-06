using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.ApplyApprovalDecisionCapability)]
public sealed class ApplyApprovalDecisionHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<ApproveProcurementRequestInput, ProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;

    public ApplyApprovalDecisionHandler(
        ProcurementApplicationService service,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<ProcurementRequestResult> ExecuteAsync(
        ApproveProcurementRequestInput input, CancellationToken ct)
    {
        RequireHumanTaskSource();
        return Task.FromResult(_service.ApplyApprovalDecision(input, TenantId, UserId));
    }
}
