using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

[CapabilityName(ProcurementContractIds.GetCapability)]
public sealed class GetProcurementRequestHandler
    : ProcurementCapabilityHandler, ICapabilityHandler<GetProcurementRequestInput, ProcurementRequestResult>
{
    private readonly ProcurementApplicationService _service;

    public GetProcurementRequestHandler(
        ProcurementApplicationService service,
        ICapabilityExecutionContextAccessor execution)
        : base(execution)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<ProcurementRequestResult> ExecuteAsync(GetProcurementRequestInput input, CancellationToken ct)
        => Task.FromResult(_service.Get(input, TenantId));
}
