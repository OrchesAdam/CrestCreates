using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityProfileResolver
{
    public sealed class EffectiveProfile
    {
        public TimeSpan? Timeout { get; init; }
        public string? RetryPolicy { get; init; }
        public bool? RequireApproval { get; init; }
        public int? RateLimit { get; init; }
    }

    public static EffectiveProfile Resolve(
        CapabilityDescriptor descriptor,
        IReadOnlyList<CapabilityProfile> profiles,
        string? tenantId = null,
        string? environment = null)
    {
        var ordered = profiles
            .Where(p => p.Capability.Id == descriptor.Id)
            .OrderByDescending(p => GetScopePriority(p.Scope, tenantId, environment));

        var result = new EffectiveProfile();

        foreach (var profile in ordered)
        {
            result = new EffectiveProfile
            {
                Timeout = profile.Timeout ?? result.Timeout,
                RetryPolicy = profile.RetryPolicy ?? result.RetryPolicy,
                RequireApproval = profile.RequireApproval ?? result.RequireApproval,
                RateLimit = profile.RateLimit ?? result.RateLimit
            };
        }

        return result;
    }

    private static int GetScopePriority(string scope, string? tenantId, string? environment)
    {
        if (tenantId != null && scope == $"Tenant:{tenantId}") return 3;
        if (environment != null && scope == $"Environment:{environment}") return 2;
        if (scope.StartsWith("Global")) return 1;
        return 0;
    }
}
