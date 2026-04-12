using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public interface IFeatureStore
{
    Task<FeatureValueEntry?> GetOrNullAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureValueEntry>> GetListAsync(
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
