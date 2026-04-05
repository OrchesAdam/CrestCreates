using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Application.Permissions;

public interface IPermissionSyncService
{
    Task SyncToDatabaseAsync(CancellationToken cancellationToken = default);
    Task SyncToAuthorizationCenterAsync(CancellationToken cancellationToken = default);
    Task SyncAllAsync(CancellationToken cancellationToken = default);
}
