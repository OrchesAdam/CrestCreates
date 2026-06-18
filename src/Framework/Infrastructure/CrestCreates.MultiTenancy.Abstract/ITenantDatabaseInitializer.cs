using System;

namespace CrestCreates.MultiTenancy.Abstract;

[Obsolete("Use ITenantDatabaseProvisioner instead.")]
public interface ITenantDatabaseInitializer : ITenantDatabaseProvisioner
{
}

public class TenantDatabaseInitializeResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantDatabaseInitializeResult Succeeded() => new() { Success = true };
    public static TenantDatabaseInitializeResult Failed(string error) => new() { Success = false, Error = error };
}
