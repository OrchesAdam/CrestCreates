using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Sample.AssetManagement.Contracts.Json;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetRuntimeStateContractContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        builder.Add("crest.sample.asset-management.maintenance-decision/v1", AssetJsonContext.Default.AssetMaintenanceDecisionFact, new HashSet<Type> { typeof(CrestCreates.Sample.AssetManagement.Contracts.AssetMaintenanceDecisionFact) });
        builder.Add("crest.sample.asset-management.production-lock/v1", AssetJsonContext.Default.AssetProductionLockFact, new HashSet<Type> { typeof(CrestCreates.Sample.AssetManagement.Contracts.AssetProductionLockFact) });
        builder.Add("crest.sample.asset-management.maintenance-evidence/v1", AssetJsonContext.Default.AssetMaintenanceEvidenceFact, new HashSet<Type> { typeof(CrestCreates.Sample.AssetManagement.Contracts.AssetMaintenanceEvidenceFact) });
        builder.Add("crest.sample.asset-management.production-release/v1", AssetJsonContext.Default.AssetProductionReleaseFact, new HashSet<Type> { typeof(CrestCreates.Sample.AssetManagement.Contracts.AssetProductionReleaseFact) });
    }
}
