using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.SubmitCapability)]
public sealed class SubmitProcurementRequestHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<SubmitProcurementRequestInput, SubmitProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;

    public SubmitProcurementRequestHandler(
        ProcurementApplicationService service,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<SubmitProcurementRequestResult> ExecuteAsync(
        SubmitProcurementRequestInput input, CancellationToken ct)
        => _service.SubmitAsync(input, TenantId, UserId, ct);
}
