using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.ApplyRejectionDecisionCapability)]
public sealed class ApplyRejectionDecisionHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<RejectProcurementRequestInput, ProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;

    public ApplyRejectionDecisionHandler(
        ProcurementApplicationService service,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<ProcurementRequestResult> ExecuteAsync(
        RejectProcurementRequestInput input, CancellationToken ct)
    {
        RequireHumanTaskSource();
        return Task.FromResult(_service.ApplyRejectionDecision(input, TenantId, UserId));
    }
}
