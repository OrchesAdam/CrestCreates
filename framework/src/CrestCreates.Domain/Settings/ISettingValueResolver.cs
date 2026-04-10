using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Settings;

public interface ISettingValueResolver
{
    Task<ResolvedSettingValue> ResolveAsync(
        string name,
        string? tenantId = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolvedSettingValue>> ResolveAllAsync(
        string? groupName = null,
        string? tenantId = null,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
