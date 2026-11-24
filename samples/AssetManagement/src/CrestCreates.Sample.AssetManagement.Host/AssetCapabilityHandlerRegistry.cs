using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Sample.AssetManagement.Application.Handlers;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetCapabilityHandlerRegistry : ICapabilityHandlerRegistry
{
    public IReadOnlyDictionary<string, Type> GetHandlerMappings() => new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [AssetContractIds.RegisterCapability] = typeof(RegisterAssetHandler),
        [AssetContractIds.GetCapability] = typeof(GetAssetHandler),
        [AssetContractIds.QueryCapability] = typeof(QueryAssetsHandler),
        [AssetContractIds.UpdateCapability] = typeof(UpdateAssetHandler),
        [AssetContractIds.AssignCapability] = typeof(AssignAssetHandler),
        [AssetContractIds.ReturnCapability] = typeof(ReturnAssetHandler),
        [AssetContractIds.TransferCapability] = typeof(TransferAssetHandler),
        [AssetContractIds.RequestMaintenanceCapability] = typeof(RequestMaintenanceHandler),
        [AssetContractIds.ApplyMaintenanceCapability] = typeof(ApplyMaintenanceDecisionHandler),
        [AssetProductionDescriptors.ReleaseCapabilityId] = typeof(AssetProductionReleaseHandler)
    };
}
