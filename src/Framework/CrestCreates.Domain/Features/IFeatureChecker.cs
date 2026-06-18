using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Features;

public interface IFeatureChecker
{
    Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default);

    Task<bool> IsEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default);
}
