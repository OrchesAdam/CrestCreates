using CrestCreates.DynamicApi;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Host.Endpoints;

[CapabilityEndpointSpecs]
public static partial class ProcurementEndpoints
{
    [CapabilityEndpointSpec(
        "procurement.submit-request",
        CapabilityEndpointHttpMethod.Post,
        "/api/procurement/requests",
        SuccessStatusCode = 201,
        GroupName = "Procurement",
        Tags = new[] { "Procurement" })]
    [CapabilityEndpointInput(typeof(SubmitProcurementRequestInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    public sealed partial class SubmitRequest { }

    [CapabilityEndpointSpec(
        "procurement.approve-request",
        CapabilityEndpointHttpMethod.Post,
        "/api/procurement/requests/{requestId:guid}/approve",
        SuccessStatusCode = 200,
        GroupName = "Procurement",
        Tags = new[] { "Procurement" })]
    [CapabilityEndpointInput(typeof(ApproveProcurementRequestInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "requestId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(ApproveProcurementRequestInput.RequestId))]
    public sealed partial class ApproveRequest { }

    [CapabilityEndpointSpec(
        "procurement.reject-request",
        CapabilityEndpointHttpMethod.Post,
        "/api/procurement/requests/{requestId:guid}/reject",
        SuccessStatusCode = 200,
        GroupName = "Procurement",
        Tags = new[] { "Procurement" })]
    [CapabilityEndpointInput(typeof(RejectProcurementRequestInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "requestId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(RejectProcurementRequestInput.RequestId))]
    public sealed partial class RejectRequest { }

    [CapabilityEndpointSpec(
        "procurement.get-request",
        CapabilityEndpointHttpMethod.Get,
        "/api/procurement/requests/{requestId:guid}",
        SuccessStatusCode = 200,
        GroupName = "Procurement",
        Tags = new[] { "Procurement" })]
    [CapabilityEndpointInput(typeof(GetProcurementRequestInput), Name = "body", Source = CapabilityEndpointParameterSource.Body, Required = false)]
    [CapabilityEndpointInput(typeof(Guid), Name = "requestId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(GetProcurementRequestInput.RequestId))]
    public sealed partial class GetRequest { }
}
