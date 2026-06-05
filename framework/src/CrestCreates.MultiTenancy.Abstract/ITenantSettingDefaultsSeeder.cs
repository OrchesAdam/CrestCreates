using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantSettingDefaultsSeeder
{
    Task<TenantSettingDefaultsResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantSettingDefaultsResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantSettingDefaultsResult Succeeded() => new() { Success = true };
    public static TenantSettingDefaultsResult Failed(string error) => new() { Success = false, Error = error };
}
