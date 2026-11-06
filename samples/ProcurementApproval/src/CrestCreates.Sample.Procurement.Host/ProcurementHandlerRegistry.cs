using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Sample.Procurement.Application.Handlers;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class ProcurementHandlerRegistry : ICapabilityHandlerRegistry
{
    public IReadOnlyDictionary<string, Type> GetHandlerMappings() => new Dictionary<string, Type>
    {
        ["procurement.submit-request"] = typeof(SubmitProcurementRequestHandler),
        ["procurement.approve-request"] = typeof(ApproveProcurementRequestHandler),
        ["procurement.reject-request"] = typeof(RejectProcurementRequestHandler),
        ["procurement.request.apply-approval"] = typeof(ApplyApprovalDecisionHandler),
        ["procurement.request.apply-rejection"] = typeof(ApplyRejectionDecisionHandler),
        ["procurement.get-request"] = typeof(GetProcurementRequestHandler),
    };
}
