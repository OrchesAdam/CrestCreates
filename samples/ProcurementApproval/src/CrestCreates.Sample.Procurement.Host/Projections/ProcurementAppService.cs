using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.Projections;

[CrestService]
[CapabilityCompatibilityProjection(RoutePrefix = "api/procurement")]
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

    public Task<SubmitProcurementRequestResult> SubmitAsync(
        SubmitProcurementRequestInput input,
        CancellationToken cancellationToken = default)
    {
        var context = RequiredContext();
        return _application.SubmitAsync(
            input,
            context.TenantId!,
            context.UserId!,
            cancellationToken);
    }

    public Task<ProcurementRequestResult> GetAsync(Guid requestId)
    {
        var context = RequiredContext();
        return Task.FromResult(_application.Get(
            new GetProcurementRequestInput { RequestId = requestId },
            context.TenantId!));
    }

    public Task<ProcurementRequestResult> ApproveAsync(ApproveProcurementRequestInput input)
    {
        var context = RequiredContext();
        return Task.FromResult(_application.Approve(input, context.TenantId!, context.UserId!));
    }

    public Task<ProcurementRequestResult> RejectAsync(RejectProcurementRequestInput input)
    {
        var context = RequiredContext();
        return Task.FromResult(_application.Reject(input, context.TenantId!, context.UserId!));
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
