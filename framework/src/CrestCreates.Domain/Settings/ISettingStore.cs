using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public interface ISettingStore
{
    Task<SettingValueEntry?> GetOrNullAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueEntry>> GetListAsync(
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
