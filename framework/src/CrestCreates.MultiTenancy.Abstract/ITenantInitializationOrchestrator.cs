using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantInitializationOrchestrator
{
    Task<TenantInitializationResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
