using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantDataSeedContributor
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
