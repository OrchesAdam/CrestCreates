using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Features;

public interface IFeatureProvider
{
    Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default);

    Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolvedFeatureValue>> GetAllAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);
}
