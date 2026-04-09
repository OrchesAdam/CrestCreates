using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Permissions;

namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionGrantStore
{
    Task<IReadOnlyList<PermissionGrantInfo>> GetGrantsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetGrantedPermissionsAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        string? tenantId,
        CancellationToken cancellationToken = default);
}
