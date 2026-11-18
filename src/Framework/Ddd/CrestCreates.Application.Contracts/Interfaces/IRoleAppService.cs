using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Contracts.Permissions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IRoleAppService
{
    [AppServiceHttpAction(Verb = HttpProjectionVerb.Post, Route = "", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task<IdentityRoleDto> CreateAsync(CreateIdentityRoleDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "{roleId}", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Users.Read })]
    Task<IdentityRoleDto?> GetAsync(Guid roleId, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Get, Route = "", Kind = HttpProjectionOperationKind.Query,
        Permissions = new[] { PlatformHttpPermissions.Users.Read })]
    Task<IReadOnlyList<IdentityRoleDto>> GetListAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Put, Route = "{roleId}", Kind = HttpProjectionOperationKind.Command,
        Body = HttpProjectionBodyPolicy.Required, Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task<IdentityRoleDto> UpdateAsync(Guid roleId, UpdateIdentityRoleDto input, CancellationToken cancellationToken = default);

    [AppServiceHttpAction(Verb = HttpProjectionVerb.Patch, Route = "{roleId}/active", Kind = HttpProjectionOperationKind.Command,
        Permissions = new[] { PlatformHttpPermissions.Users.Manage })]
    Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken cancellationToken = default);
}