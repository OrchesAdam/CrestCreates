namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Locks the authoritative identity source of the tenant main chain: at these sites the value entering the
/// identity slot must textually originate from an id member, not merely "not a name". A change to the chain
/// that swaps in another source fails here even when the value flow analysis stays green.
/// </summary>
internal static class TenantIdentityAuthoritativeLedger
{
    private static readonly (string FileFragment, string PortFragment, string Requirement)[] Rows =
    [
        ("MultiTenancy/Middleware/MultiTenancyMiddleware.cs", "ICurrentTenant.ChangeAsync", "resolutionResult.TenantId"),
        ("MultiTenancy/Resolvers/HeaderTenantResolver.cs", "TenantResolutionResult.Success", "tenant.Id"),
        ("MultiTenancy/Resolvers/OtherTenantResolvers.cs", "TenantResolutionResult.Success", "tenant.Id"),
        ("MultiTenancy/Resolvers/SubdomainTenantResolver.cs", "TenantResolutionResult.Success", "tenant.Id"),
        ("MultiTenancy/TenantResolutionResult.cs", "TenantResolutionResult.Success", "tenant.Id"),
        ("Infrastructure/Authorization/IdentityClaimsBuilder.cs", "Claim(tenantid|tenant_id)", "context.TenantId"),
        ("CrestCreates.Authorization/AuditTenantContextResolver.cs", "GetTenantCacheKey", "ResolveTenantIdOrNull"),
        ("Application/Tenants/TenantCacheInvalidator.cs", "GetTenantCacheKey", "tenantId parameter"),
        ("CrestCreates.Caching/TenantCacheKeyContributor.cs", "GetTenantCacheKey", "tenantId parameter"),
    ];

    public static IReadOnlyList<string> LockedFiles => Rows.Select(row => row.FileFragment).ToArray();

    public static IEnumerable<string> Evaluate(TenantIdentityAnalysis analysis)
    {
        foreach (var (fileFragment, portFragment, requirement) in Rows)
        {
            var sites = analysis.Exits
                .Where(exit => exit.RelativePath.Contains(fileFragment, StringComparison.OrdinalIgnoreCase)
                               && exit.PortLabel.Contains(portFragment, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (sites.Length == 0)
            {
                yield return $"locked identity site is missing: {fileFragment} :: {portFragment} ({requirement})";
                continue;
            }

            foreach (var site in sites.Where(site => !TenantIdentitySymbols.IsAuthoritativeIdShape(site.TerminalNames)))
            {
                yield return $"{site.Rule} {site.RelativePath}({site.Line},{site.Column}): {site.PortLabel} receives '{site.ArgumentText}' but the locked source is {requirement}";
            }
        }
    }
}
