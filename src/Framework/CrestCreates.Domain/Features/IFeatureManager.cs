using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public interface IFeatureManager
{
    Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default);

    Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default);

    Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default);

    Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default);

    Task<FeatureValueEntry?> GetScopedValueOrNullAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureValueEntry>> GetScopedValuesAsync(
        FeatureScope scope,
        string providerKey,
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
