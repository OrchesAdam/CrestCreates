namespace CrestCreates.DependencyBoundaries.Tests;

internal enum TenantIdentityRule
{
    /// <summary>A tenant name value reached a tenant context frame or an identity claim slot.</summary>
    TI001,

    /// <summary>A tenant name value reached authorization, data filtering, cache or connection partitioning.</summary>
    TI002,

    /// <summary>A tenant name value masquerades as identity: getter, return contract or constructor id slot.</summary>
    TI003,

    /// <summary>Coverage failure: a sensitive node did not bind, or a name-carrying flow could not be decided.</summary>
    TI900,
}

internal sealed record TenantIdentityDiagnostic(
    TenantIdentityRule Rule,
    string Project,
    string RelativePath,
    int Line,
    int Column,
    string Source,
    string Exit,
    string Hint)
{
    public override string ToString() =>
        $"{Rule} {Project} {RelativePath}({Line},{Column}): source={Source} exit={Exit} -> {Hint}";
}

/// <summary>
/// Tenant identity port table. This is test-internal code: it is not a production registry,
/// analyzer descriptor or runtime extension point.
/// </summary>
internal static class TenantIdentityPorts
{
    public const string CurrentTenantType = "CrestCreates.MultiTenancy.Abstract.ICurrentTenant";
    public const string TenantInfoType = "CrestCreates.MultiTenancy.Abstract.ITenantInfo";
    public const string TenantContextType = "CrestCreates.MultiTenancy.Abstract.ITenantContext";
    public const string ResolutionResultType = "CrestCreates.MultiTenancy.Abstract.TenantResolutionResult";
    public const string EventContextAccessorType = "CrestCreates.EventBus.ITenantEventContextAccessor";
    public const string CacheKeyContributorType = "CrestCreates.Caching.TenantCacheKeyContributor";
    public const string MultiTenantEntityType = "CrestCreates.DataFilter.Entities.IMultiTenant";
    public const string TenantEntityType = "CrestCreates.Domain.Permission.Tenant";
    public const string ClaimType = "System.Security.Claims.Claim";

    public static readonly string[] TenantClaimTypes = ["tenantid", "tenant_id"];

    /// <summary>
    /// Explicit identity slots: argument position that must always carry the authoritative tenant id.
    /// </summary>
    public static readonly InvocationPort[] InvocationPorts =
    [
        new(CurrentTenantType, "ChangeAsync", 0, TenantIdentityRule.TI001, "ICurrentTenant.ChangeAsync(tenantId)"),
        new(CurrentTenantType, "SetTenantId", 0, TenantIdentityRule.TI001, "ICurrentTenant.SetTenantId(tenantId)"),
        new(ResolutionResultType, "Success", 0, TenantIdentityRule.TI001, "TenantResolutionResult.Success(tenantId, tenantName, ..) slot 0"),
        new(EventContextAccessorType, "SetTenantContext", 0, TenantIdentityRule.TI001, "ITenantEventContextAccessor.SetTenantContext(tenantId, tenantName, ..) slot 0"),
        new(CacheKeyContributorType, "GetTenantCacheKey", 0, TenantIdentityRule.TI002, "TenantCacheKeyContributor.GetTenantCacheKey(tenantId, ..)"),
        new(CacheKeyContributorType, "GetPermissionCacheKey", 0, TenantIdentityRule.TI002, "TenantCacheKeyContributor.GetPermissionCacheKey(tenantId, ..)"),
        new(CacheKeyContributorType, "GetAuthorizationCacheKey", 0, TenantIdentityRule.TI002, "TenantCacheKeyContributor.GetAuthorizationCacheKey(tenantId, ..)"),
    ];

    /// <summary>Method names that mark a syntactic exit candidate even without a tenant token.</summary>
    public static readonly string[] ExitMemberTokens =
    [
        "changeasync",
        "settenantid",
        "settenantcontext",
        "gettenantcachekey",
        "getpermissioncachekey",
        "getauthorizationcachekey",
    ];

    /// <summary>
    /// Property names on the tenant entity and <c>ITenantInfo</c> implementations that carry a display name.
    /// </summary>
    public static readonly string[] TenantNamePropertyNames = ["Name", "NormalizedName", "DisplayName"];

    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var c in value)
        {
            if (c is '_' or '-' or ' ' or '.')
            {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..index]);
    }

    /// <summary>
    /// Identity slot by declaration name: parameters, properties and fields whose name denotes the tenant id.
    /// </summary>
    public static bool IsIdentityMemberName(string name)
    {
        var normalized = Normalize(name);
        return normalized == "tenantid"
            || normalized == "currenttenantid"
            || normalized.EndsWith("tenantid", StringComparison.Ordinal);
    }

    /// <summary>
    /// Name-bearing declaration: parameters, properties, fields and locals whose name denotes a tenant name.
    /// A bare <c>Name</c> is never a source by itself: it only counts on the tenant entity or an
    /// <c>ITenantInfo</c> implementation, so display names of unrelated objects are not reported.
    /// </summary>
    public static bool IsTenantNameMemberName(string name)
    {
        var normalized = Normalize(name);
        return normalized == "tenantname"
            || normalized.EndsWith("tenantname", StringComparison.Ordinal)
            || normalized.EndsWith("tenantnormalizedName", StringComparison.Ordinal);
    }

    public sealed record InvocationPort(
        string DeclaringTypeFullName,
        string MethodName,
        int Slot,
        TenantIdentityRule Rule,
        string Label);
}
