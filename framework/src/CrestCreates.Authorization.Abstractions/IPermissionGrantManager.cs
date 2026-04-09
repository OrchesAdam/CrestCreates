using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionGrantManager
{
    Task GrantAsync(PermissionGrantInfo grant, CancellationToken cancellationToken = default);

    Task RevokeAsync(PermissionGrantInfo grant, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGrantInfo>> GetGrantsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(
        string userId,
        IEnumerable<string> roleNames,
        string? tenantId,
        CancellationToken cancellationToken = default);
}
