using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IPermissionGrantAppService
{
    Task GrantToUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default);

    Task GrantToRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default);

    Task RevokeFromUserAsync(
        string userId,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default);

    Task RevokeFromRoleAsync(
        string roleName,
        PermissionGrantChangeDto input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGrantDto>> GetUserGrantsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGrantDto>> GetRoleGrantsAsync(
        string roleName,
        CancellationToken cancellationToken = default);

    Task<UserEffectivePermissionsDto> GetUserEffectivePermissionsAsync(
        string userId,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
