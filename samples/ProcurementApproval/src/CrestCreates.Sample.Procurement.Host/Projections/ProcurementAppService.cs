using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.Projections;

[CrestService]
[CapabilityCompatibilityProjection(RoutePrefix = "api/procurement/query")]
public sealed class ProcurementAppService
{
    private readonly ProcurementApplicationService _application;
    private readonly ICapabilityExecutionContextAccessor _execution;

    public ProcurementAppService(
        ProcurementApplicationService application,
        ICapabilityExecutionContextAccessor execution)
    {
        _application = application;
        _execution = execution;
    }

    public Task<ProcurementRequestResult> GetAsync(Guid requestId)
    {
        var context = RequiredContext();
        return Task.FromResult(_application.Get(
            new GetProcurementRequestInput { RequestId = requestId },
            context.TenantId!));
    }

    private CapabilityExecutionContext RequiredContext()
    {
        var context = _execution.Current;
        if (context is null
            || string.IsNullOrWhiteSpace(context.TenantId)
            || string.IsNullOrWhiteSpace(context.UserId))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_CONTEXT_REQUIRED",
                "A trusted tenant and user context is required.");
        }

        return context;
    }
}
