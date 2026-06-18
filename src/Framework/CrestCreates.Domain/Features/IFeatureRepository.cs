using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public interface IFeatureRepository : ICrestRepositoryBase<FeatureValue, System.Guid>
{
    Task<FeatureValue?> FindAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<List<FeatureValue>> GetListByScopeAsync(
        FeatureScope scope,
        string? providerKey = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
