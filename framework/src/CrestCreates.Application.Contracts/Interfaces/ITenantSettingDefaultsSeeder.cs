using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantSettingDefaultsSeeder
{
    Task<TenantSettingDefaultsResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantSettingDefaultsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantSettingDefaultsResult Succeeded() => new() { Success = true };
    public static TenantSettingDefaultsResult Failed(string error) => new() { Success = false, Error = error };
}
