using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantMigrationRunner
{
    Task<TenantMigrationResult> RunAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantMigrationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantMigrationResult Succeeded() => new() { Success = true };
    public static TenantMigrationResult Failed(string error) => new() { Success = false, Error = error };
}
