using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IFeatureAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "global", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<List<FeatureValueDto>> GetGlobalValuesAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "tenant/{tenantId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "current-tenant", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync(CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "global/{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<FeatureValueDto?> GetGlobalValueAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "tenant/{tenantId}/{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<FeatureValueDto?> GetTenantValueAsync(string name, string tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "current-tenant/{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "global/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Features.ManageGlobal })]
    Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "tenant/{tenantId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Features.ManageTenant })]
    Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "global/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Features.ManageGlobal })]
    Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "tenant/{tenantId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Features.ManageTenant })]
    Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "enabled/{featureName}", Kind = HttpProjectionOperationKind.Query)]
    Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "tenant/{tenantId}/enabled/{featureName}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Features.Read })]
    Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default);
}