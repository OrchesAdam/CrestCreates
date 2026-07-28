using CrestCreates.DynamicApi;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.Endpoints;

[CapabilityEndpointSet(RoutePrefix = "api/procurement", GroupName = "Procurement", Tags = new[] { "Procurement" })]
public static partial class ProcurementEndpoints
{
    [Post("procurement.submit-request", "requests", Body = typeof(SubmitProcurementRequestInput), SuccessStatusCode = 201)]
    public sealed partial class SubmitRequest { }

    [Post("procurement.approve-request", "requests/{requestId:guid}/approve", Body = typeof(ApproveProcurementRequestInput))]
    public sealed partial class ApproveRequest { }

    [Post("procurement.reject-request", "requests/{requestId:guid}/reject", Body = typeof(RejectProcurementRequestInput))]
    public sealed partial class RejectRequest { }

    [Get("procurement.get-request", "requests/{requestId:guid}", Input = typeof(Guid))]
    public sealed partial class GetRequest { }
}
