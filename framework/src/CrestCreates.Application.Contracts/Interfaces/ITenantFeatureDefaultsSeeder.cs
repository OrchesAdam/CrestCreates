using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantFeatureDefaultsSeeder
{
    Task<TenantFeatureDefaultsResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantFeatureDefaultsResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantFeatureDefaultsResult Succeeded() => new() { Success = true };
    public static TenantFeatureDefaultsResult Failed(string error) => new() { Success = false, Error = error };
}
