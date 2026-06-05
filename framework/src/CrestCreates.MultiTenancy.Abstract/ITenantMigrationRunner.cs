using System;

namespace CrestCreates.MultiTenancy.Abstract;

[Obsolete("Use ITenantSchemaMigrator instead.")]
public interface ITenantMigrationRunner : ITenantSchemaMigrator
{
}

public class TenantMigrationResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantMigrationResult Succeeded() => new() { Success = true };
    public static TenantMigrationResult Failed(string error) => new() { Success = false, Error = error };
}
