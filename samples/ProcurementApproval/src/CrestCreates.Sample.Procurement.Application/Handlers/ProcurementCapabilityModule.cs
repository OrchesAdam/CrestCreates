using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ProcurementCapabilityModule : ICapabilityHandlerModule
{
    public string Id => "procurement";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        resolver.Register("procurement.submit-request", new SubmitProcurementRequestHandler());
        resolver.Register("procurement.approve-request", new ApproveProcurementRequestHandler());
        resolver.Register("procurement.reject-request", new RejectProcurementRequestHandler());
    }
}
