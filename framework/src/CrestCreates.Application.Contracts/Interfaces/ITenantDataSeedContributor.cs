using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantDataSeedContributor
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
