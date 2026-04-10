using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public interface ISettingManager
{
    Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default);

    Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default);

    Task SetUserAsync(
        string name,
        string userId,
        string? value,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default);

    Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default);

    Task RemoveUserAsync(
        string name,
        string userId,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<SettingValueEntry?> GetScopedValueOrNullAsync(
        string name,
        SettingScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingValueEntry>> GetScopedValuesAsync(
        SettingScope scope,
        string providerKey,
        string? groupName = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default);
}
