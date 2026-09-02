using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Sample.AssetManagement.Contracts.Json;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
        => builder.Add("crest.sample.asset-management.maintenance-decision/v1", AssetJsonContext.Default.AssetMaintenanceDecisionFact, new HashSet<Type> { typeof(CrestCreates.Sample.AssetManagement.Contracts.AssetMaintenanceDecisionFact) });
}
