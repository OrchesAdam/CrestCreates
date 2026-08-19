using CrestCreates.Organization.Abstractions;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Runner-free Rule store contract primitives shared by provider runners.
/// </summary>
public static class DataPermissionScopeRuleStoreContractCases
{
    public static async Task ExactTenantAsync(
        IDataPermissionScopeRuleStore store,
        string resource,
        string tenantId,
        DataPermissionScopeKind expected)
    {
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = resource,
            Action = "read",
            Permission = "view",
            TenantId = tenantId,
            ScopeKind = expected
        });

        var actual = await store.GetScopeKindAsync(resource, "read", "view", tenantId);
        if (actual != expected)
            throw new InvalidOperationException($"Expected scope '{expected}', got '{actual}'.");
    }
}
