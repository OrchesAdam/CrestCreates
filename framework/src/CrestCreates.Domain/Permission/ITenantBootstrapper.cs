using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Permission;

public interface ITenantBootstrapper
{
    Task BootstrapAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default);
}
