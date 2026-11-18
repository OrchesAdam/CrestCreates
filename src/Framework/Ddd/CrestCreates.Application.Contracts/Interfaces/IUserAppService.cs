using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IUserAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task<IdentityUserDto> CreateAsync(CreateIdentityUserDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{userId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Users.Read })]
    Task<IdentityUserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Users.Read })]
    Task<IReadOnlyList<IdentityUserDto>> GetListAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{userId}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task<IdentityUserDto> UpdateAsync(Guid userId, UpdateIdentityUserDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Patch, Route = "{userId}/active", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{userId}/change-password", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required)]
    Task ChangePasswordAsync(Guid userId, ChangeIdentityPasswordDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "{userId}/roles/{roleId}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Delete, Route = "{userId}/roles/{roleId}", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{userId}/roles", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Users.Read })]
    Task<IReadOnlyList<IdentityUserRoleDto>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);
}