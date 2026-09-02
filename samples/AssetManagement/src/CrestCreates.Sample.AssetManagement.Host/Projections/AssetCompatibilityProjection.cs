using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Host.Projections;

[CrestService]
[CapabilityCompatibilityProjection(RoutePrefix = "api/assets/compat")]
public sealed class AssetCompatibilityProjection(AssetApplicationService application, ICapabilityExecutionContextAccessor execution)
{
    public async Task<AssetResult?> GetAsync(Guid assetId)
    {
        var context = RequiredContext();
        return await application.GetAsync(assetId, context.TenantId!, CancellationToken.None);
    }

    public async Task<IReadOnlyList<AssetResult>> ListAsync()
    {
        var context = RequiredContext();
        return await application.QueryAsync(new AssetQueryInput(), context.TenantId!, CancellationToken.None);
    }

    private CapabilityExecutionContext RequiredContext()
        => execution.Current is { TenantId: not null, UserId: not null } context
            ? context
            : throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A trusted tenant and user context is required.");
}
