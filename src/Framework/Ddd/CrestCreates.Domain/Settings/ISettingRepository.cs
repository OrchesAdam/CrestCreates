using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public interface ISettingRepository : ICrestRepositoryBase<SettingValue, System.Guid>
{
    Task<SettingValue?> FindAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<List<SettingValue>> GetListByScopeAsync(
        SettingScope scope,
        string? providerKey = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
