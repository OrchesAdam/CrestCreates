using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ISettingAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "current", Kind = HttpProjectionOperationKind.Query)]
    Task<IReadOnlyList<SettingValueDto>> GetCurrentValuesAsync(string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "global", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingValueDto>> GetGlobalValuesAsync(string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "current-tenant", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingValueDto>> GetCurrentTenantValuesAsync(string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "tenant/{tenantId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingValueDto>> GetTenantValuesAsync(string tenantId, string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "current-user", Kind = HttpProjectionOperationKind.Query)]
    Task<IReadOnlyList<SettingValueDto>> GetCurrentUserValuesAsync(string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "user/{userId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Settings.Read })]
    Task<IReadOnlyList<SettingValueDto>> GetUserValuesAsync(string userId, string? tenantId = null, string? groupName = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "global/{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task UpdateGlobalAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "current-tenant/{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task UpdateCurrentTenantAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "tenant/{tenantId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task UpdateTenantAsync(string tenantId, string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "current-user/{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required)]
    Task UpdateCurrentUserAsync(string name, UpdateSettingValueDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "user/{userId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task UpdateUserAsync(string userId, string name, UpdateSettingValueDto input, string? tenantId = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "global/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task DeleteGlobalAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "current-tenant/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task DeleteCurrentTenantAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "tenant/{tenantId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task DeleteTenantAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "current-user/{name}", Kind = HttpProjectionOperationKind.Command)]
    Task DeleteCurrentUserAsync(string name, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "user/{userId}/{name}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Settings.Manage })]
    Task DeleteUserAsync(string userId, string name, string? tenantId = null, CancellationToken cancellationToken = default);
}