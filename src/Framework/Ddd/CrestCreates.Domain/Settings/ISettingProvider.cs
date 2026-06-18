using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Settings;

public interface ISettingProvider
{
    Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default);

    Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResolvedSettingValue>> GetAllAsync(
        string? groupName = null,
        CancellationToken cancellationToken = default);
}
