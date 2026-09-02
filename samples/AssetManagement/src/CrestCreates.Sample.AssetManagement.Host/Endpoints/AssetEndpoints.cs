using CrestCreates.DynamicApi;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Host.Endpoints;

[CapabilityEndpointSpecs]
public static partial class AssetEndpoints
{
    [CapabilityEndpointSpec(AssetContractIds.RegisterCapability, CapabilityEndpointHttpMethod.Post, "/api/assets", SuccessStatusCode = 201, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(RegisterAssetInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    public sealed partial class Register;

    [CapabilityEndpointSpec(AssetContractIds.GetCapability, CapabilityEndpointHttpMethod.Get, "/api/assets/{assetId:guid}", GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(AssetQueryInput), Name = "body", Source = CapabilityEndpointParameterSource.Body, Required = false)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(AssetQueryInput.AssetId))]
    public sealed partial class Get;

    [CapabilityEndpointSpec(AssetContractIds.QueryCapability, CapabilityEndpointHttpMethod.Post, "/api/assets/query", SuccessStatusCode = 200, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(AssetQueryInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    public sealed partial class Query;

    [CapabilityEndpointSpec(AssetContractIds.UpdateCapability, CapabilityEndpointHttpMethod.Put, "/api/assets/{assetId:guid}", SuccessStatusCode = 200, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(UpdateAssetInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(UpdateAssetInput.AssetId))]
    public sealed partial class Update;

    [CapabilityEndpointSpec(AssetContractIds.AssignCapability, CapabilityEndpointHttpMethod.Post, "/api/assets/{assetId:guid}/assign", SuccessStatusCode = 200, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(AssignAssetInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(AssignAssetInput.AssetId))]
    public sealed partial class Assign;

    [CapabilityEndpointSpec(AssetContractIds.ReturnCapability, CapabilityEndpointHttpMethod.Post, "/api/assets/{assetId:guid}/return", SuccessStatusCode = 200, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(AssetIdInput), Name = "body", Source = CapabilityEndpointParameterSource.Body, Required = false)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(AssetIdInput.AssetId))]
    public sealed partial class Return;

    [CapabilityEndpointSpec(AssetContractIds.TransferCapability, CapabilityEndpointHttpMethod.Post, "/api/assets/{assetId:guid}/transfer", SuccessStatusCode = 200, GroupName = "Asset Management", Tags = new[] { "Assets" })]
    [CapabilityEndpointInput(typeof(TransferAssetInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(TransferAssetInput.AssetId))]
    public sealed partial class Transfer;

    [CapabilityEndpointSpec(AssetContractIds.RequestMaintenanceCapability, CapabilityEndpointHttpMethod.Post, "/api/assets/{assetId:guid}/maintenance", SuccessStatusCode = 202, GroupName = "Asset Management", Tags = new[] { "Maintenance" })]
    [CapabilityEndpointInput(typeof(MaintenanceRequestInput), Name = "body", Source = CapabilityEndpointParameterSource.Body)]
    [CapabilityEndpointInput(typeof(Guid), Name = "assetId", Source = CapabilityEndpointParameterSource.Route, TargetProperty = nameof(MaintenanceRequestInput.AssetId))]
    public sealed partial class RequestMaintenance;
}
