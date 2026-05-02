using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface ITenantDatabaseInitializer
{
    Task<TenantDatabaseInitializeResult> InitializeAsync(
        TenantInitializationContext context,
        CancellationToken cancellationToken = default);
}

public class TenantDatabaseInitializeResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantDatabaseInitializeResult Succeeded() => new() { Success = true };
    public static TenantDatabaseInitializeResult Failed(string error) => new() { Success = false, Error = error };
}
