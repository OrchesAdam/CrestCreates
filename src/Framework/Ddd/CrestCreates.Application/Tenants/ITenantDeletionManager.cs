using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;

namespace CrestCreates.Application.Tenants;

public interface ITenantDeletionManager
{
    Task<Tenant> ArchiveAsync(string name, CancellationToken cancellationToken = default);
    Task<Tenant> RestoreAsync(string name, CancellationToken cancellationToken = default);
    Task<Tenant> SoftDeleteAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
