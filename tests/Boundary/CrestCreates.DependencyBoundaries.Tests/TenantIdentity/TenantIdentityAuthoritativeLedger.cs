namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Locks the authoritative identity source of the tenant main chain. Each row states where the value entering
/// the identity slot must provably come from, and the check runs against the evaluated origin set — a local
/// variable named <c>tenantId</c> that was assigned from a user name is a different source, not an identity.
/// </summary>
internal static class TenantIdentityAuthoritativeLedger
{
    private const string ResolutionTenantId = "CrestCreates.MultiTenancy.Abstract.TenantResolutionResult.TenantId";
    private const string TenantEntityId = "CrestCreates.Domain.Permission.Tenant.Id";
    private const string InheritedEntityId = "CrestCreates.Domain.Entities.Entity.Id";
    private const string ClaimsContextTenantId = "CrestCreates.Infrastructure.Authorization.IdentityClaimsContext.TenantId";
    private const string CurrentTenantId = "CrestCreates.MultiTenancy.Abstract.ICurrentTenant.Id";
    private const string CurrentUserTenantId = "CrestCreates.Authorization.Abstractions.ICurrentUser.TenantId";
    private const string ResolveTenantIdMethod = "method:CrestCreates.Authorization.AuditTenantContextResolver.ResolveTenantIdOrNull";

    private static readonly Row[] Rows =
    [
        new("MultiTenancy/Middleware/MultiTenancyMiddleware.cs", "ICurrentTenant.ChangeAsync", SourceRule.Exact, ResolutionTenantId),
        new("MultiTenancy/Resolvers/HeaderTenantResolver.cs", "TenantResolutionResult.Success", SourceRule.Exact, TenantEntityId, InheritedEntityId),
        new("MultiTenancy/Resolvers/OtherTenantResolvers.cs", "TenantResolutionResult.Success", SourceRule.Exact, TenantEntityId, InheritedEntityId),
        new("MultiTenancy/Resolvers/SubdomainTenantResolver.cs", "TenantResolutionResult.Success", SourceRule.Exact, TenantEntityId, InheritedEntityId),
        new("MultiTenancy/TenantResolutionResult.cs", "TenantResolutionResult.Success", SourceRule.Exact, TenantEntityId, InheritedEntityId),
        new("Infrastructure/Authorization/IdentityClaimsBuilder.cs", "Claim(tenantid|tenant_id)", SourceRule.Exact, ClaimsContextTenantId),
        new("CrestCreates.Authorization/AuditTenantContextResolver.cs", "GetTenantCacheKey", SourceRule.Exact,
            CurrentTenantId, CurrentUserTenantId, ResolveTenantIdMethod),
        new("Application/Tenants/TenantCacheInvalidator.cs", "GetTenantCacheKey", SourceRule.IdentityParameter),
        new("CrestCreates.Caching/TenantCacheKeyContributor.cs", "GetTenantCacheKey", SourceRule.IdentityParameter),
    ];

    public static IReadOnlyList<string> LockedFiles => Rows.Select(row => row.FileFragment).ToArray();

    public static IEnumerable<string> Evaluate(TenantIdentityAnalysis analysis)
    {
        foreach (var row in Rows)
        {
            var sites = analysis.Exits
                .Where(exit => exit.RelativePath.Contains(row.FileFragment, StringComparison.OrdinalIgnoreCase)
                               && exit.PortLabel.Contains(row.PortFragment, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (sites.Length == 0)
            {
                yield return $"locked identity site is missing: {row.FileFragment} :: {row.PortFragment} ({row.Describe()})";
                continue;
            }

            foreach (var site in sites.Where(site => !row.Accepts(site.Origins)))
            {
                yield return $"{site.Rule} {site.RelativePath}({site.Line},{site.Column}): {site.PortLabel} receives "
                    + $"'{site.ArgumentText}' from [{string.Join(", ", site.Origins)}] but the locked source is {row.Describe()}";
            }
        }
    }

    private enum SourceRule
    {
        /// <summary>Every origin must be one of the listed members or methods.</summary>
        Exact,

        /// <summary>Every origin must be a parameter named <c>tenantId</c>.</summary>
        IdentityParameter,
    }

    private sealed record Row(string FileFragment, string PortFragment, SourceRule Rule, params string[] Allowed)
    {
        public bool Accepts(IReadOnlyList<string> origins)
        {
            if (origins.Count == 0)
            {
                return false;
            }

            return Rule switch
            {
                SourceRule.Exact => origins.All(origin => Allowed.Contains(origin, StringComparer.Ordinal)),
                SourceRule.IdentityParameter => origins.All(origin =>
                    origin.StartsWith("param:", StringComparison.Ordinal)
                    && origin.EndsWith(":tenantId", StringComparison.Ordinal)),
                _ => false,
            };
        }

        public string Describe() => Rule switch
        {
            SourceRule.Exact => string.Join(" | ", Allowed),
            _ => "an identity parameter named tenantId",
        };
    }
}
