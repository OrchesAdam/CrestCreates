using System;

namespace CrestCreates.MultiTenancy.Abstract;

[Obsolete("Use ITenantDataSeedContributor instead.")]
public interface ITenantDataSeeder : ITenantDataSeedContributor
{
}

public class TenantSeedResult : IPhaseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TenantSeedResult Succeeded() => new() { Success = true };
    public static TenantSeedResult Failed(string error) => new() { Success = false, Error = error };
}
