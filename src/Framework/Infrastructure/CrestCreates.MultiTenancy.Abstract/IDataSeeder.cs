using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

/// <summary>
/// Runs during application startup to seed host-level data.
/// Unlike <see cref="ITenantDataSeedContributor"/> (which runs per-tenant),
/// this runs once at application startup for the host database.
/// </summary>
public interface IDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}