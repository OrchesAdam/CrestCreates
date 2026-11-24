using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

namespace CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;

public interface IAssetMaintenanceWorkflowVersionSelector
{
    ValueTask<int?> SelectVersionAsync(Guid assetId, string tenantId, CancellationToken ct = default);
}

internal sealed class AssetMaintenanceWorkflowVersionSelector(
    IAssetStore assets, AssetApprovedRelease? release, AssetProductionRelease? productionRelease)
    : IAssetMaintenanceWorkflowVersionSelector
{
    public async ValueTask<int?> SelectVersionAsync(Guid assetId, string tenantId, CancellationToken ct = default)
    {
        var baselineVersion = AssetDescriptorCatalog.MaintenanceWorkflow.Version;
        if (productionRelease is not null)
        {
            if (tenantId != productionRelease.TenantId)
                return baselineVersion;

            var asset = await assets.GetAsync(tenantId, assetId, ct)
                ?? throw new InvalidOperationException("The asset is unavailable in the current tenant.");
            return asset.Category == AssetProductionDescriptors.CriticalCategory
                ? productionRelease.Workflow.Version
                : baselineVersion;
        }

        return release is null ? null : tenantId == release.TenantId ? release.Workflow.Version : baselineVersion;
    }
}
