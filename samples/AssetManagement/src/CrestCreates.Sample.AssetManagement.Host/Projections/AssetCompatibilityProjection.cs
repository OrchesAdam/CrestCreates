using System.Threading;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;

namespace CrestCreates.Sample.AssetManagement.Host.Projections;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(AssetCompatibilityProjection),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/assets/compat",
    CapabilityIdPrefix = "compat.appservice.asset-compatibility-projection")]
public sealed class AssetCompatibilityProjection(
    AssetApplicationService application,
    ICapabilityExecutionContextAccessor execution)
{
    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "{assetId}",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { AssetPermissions.Assets.Read })]
    public async Task<AssetResult?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var context = RequiredContext();
        return await application.GetAsync(assetId, context.TenantId!, cancellationToken);
    }

    [AppServiceHttpAction(
        Verb = HttpProjectionVerb.Get,
        Route = "list",
        Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { AssetPermissions.Assets.Search })]
    public async Task<IReadOnlyList<AssetResult>> ListAsync(CancellationToken cancellationToken = default)
    {
        var context = RequiredContext();
        return await application.QueryAsync(new AssetQueryInput(), context.TenantId!, cancellationToken);
    }

    private CapabilityExecutionContext RequiredContext()
        => execution.Current is { TenantId: not null, UserId: not null } context
            ? context
            : throw new CapabilityFailureException("CAPABILITY_CONTEXT_REQUIRED", "A trusted tenant and user context is required.");
}
