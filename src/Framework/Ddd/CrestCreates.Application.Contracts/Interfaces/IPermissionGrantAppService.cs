using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IPermissionGrantAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "user/{userId}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Manage })]
    Task GrantToUserAsync(string userId, PermissionGrantChangeDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "role/{roleName}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Manage })]
    Task GrantToRoleAsync(string roleName, PermissionGrantChangeDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "user/{userId}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Manage })]
    Task RevokeFromUserAsync(string userId, PermissionGrantChangeDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "role/{roleName}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Manage })]
    Task RevokeFromRoleAsync(string roleName, PermissionGrantChangeDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "user/{userId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Read })]
    Task<IReadOnlyList<PermissionGrantDto>> GetUserGrantsAsync(string userId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "role/{roleName}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Read })]
    Task<IReadOnlyList<PermissionGrantDto>> GetRoleGrantsAsync(string roleName, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "effective/{userId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.PermissionGrants.Read })]
    Task<UserEffectivePermissionsDto> GetUserEffectivePermissionsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
}