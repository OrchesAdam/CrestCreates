using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task<TenantDto> CreateAsync(CreateTenantDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{name}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Tenants.View })]
    Task<TenantDto?> GetAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Tenants.View })]
    Task<IReadOnlyList<TenantDto>> GetListAsync(bool? isActive = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task<TenantDto> UpdateAsync(string name, UpdateTenantDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Patch, Route = "{name}/active", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task SetActiveAsync(string name, bool isActive, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{tenantId}/retry-initialization", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task<TenantInitializationResult> RetryInitializationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{tenantId}/initialization-status", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Tenants.View })]
    Task<TenantInitializationResult> GetInitializationStatusAsync(Guid tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{tenantId}/force-retry-initialization", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task<TenantInitializationResult> ForceRetryInitializationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{tenantId}/force-fail-initialization", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Tenants.Update })]
    Task ForceFailInitializationAsync(Guid tenantId, CancellationToken cancellationToken = default);
}