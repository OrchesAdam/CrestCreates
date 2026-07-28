using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Application.Handlers;

public sealed class ProcurementCapabilityModule : ICapabilityHandlerModule
{
    private readonly InMemoryProcurementRequestStore _store;

    public ProcurementCapabilityModule(InMemoryProcurementRequestStore store)
    {
        _store = store;
    }

    public string Id => "procurement";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        resolver.Register("procurement.submit-request", new SubmitProcurementRequestHandler(_store));
        resolver.Register("procurement.approve-request", new ApproveProcurementRequestHandler(_store));
        resolver.Register("procurement.reject-request", new RejectProcurementRequestHandler(_store));
        resolver.Register("procurement.get-request", new GetProcurementRequestHandler(_store));
    }
}
