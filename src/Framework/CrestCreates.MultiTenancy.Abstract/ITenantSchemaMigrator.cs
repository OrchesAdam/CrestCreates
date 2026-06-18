using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantSchemaMigrator
{
    Task<TenantMigrationResult> RunAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
