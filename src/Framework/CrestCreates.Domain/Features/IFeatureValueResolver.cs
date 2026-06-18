using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Features;

public interface IFeatureValueResolver
{
    Task<ResolvedFeatureValue> ResolveAsync(
        string name,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolvedFeatureValue>> ResolveAllAsync(
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
