using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;

namespace CrestCreates.Sample.AssetManagement.Application.Handlers;

public sealed class AssetCapabilityModule : ICapabilityHandlerModule
{
    public string Id => "asset-management";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        resolver.Register(AssetContractIds.RegisterCapability, new RegisterAssetHandler());
        resolver.Register(AssetContractIds.GetCapability, new GetAssetHandler());
        resolver.Register(AssetContractIds.QueryCapability, new QueryAssetsHandler());
        resolver.Register(AssetContractIds.UpdateCapability, new UpdateAssetHandler());
        resolver.Register(AssetContractIds.AssignCapability, new AssignAssetHandler());
        resolver.Register(AssetContractIds.ReturnCapability, new ReturnAssetHandler());
        resolver.Register(AssetContractIds.TransferCapability, new TransferAssetHandler());
        resolver.Register(AssetContractIds.RequestMaintenanceCapability, new RequestMaintenanceHandler());
        resolver.Register(AssetContractIds.ApplyMaintenanceCapability, new ApplyMaintenanceDecisionHandler());
    }
}
