using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.Permission;

public interface ITenantManager
{
    Task<Tenant> CreateAsync(
        string name,
        string? displayName,
        string? defaultConnectionString,
        CancellationToken cancellationToken = default);

    Task<Tenant> UpdateAsync(
        string name,
        string? displayName,
        string? defaultConnectionString,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        string name,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task DeleteTenantOnlyAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default);
}
