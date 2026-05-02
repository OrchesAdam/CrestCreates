using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantDataSeeder
{
    Task<TenantSeedResult> SeedAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantSeedResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantSeedResult Succeeded() => new() { Success = true };
    public static TenantSeedResult Failed(string error) => new() { Success = false, Error = error };
}
