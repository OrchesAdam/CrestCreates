using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantDatabaseProvisioner
{
    Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}
