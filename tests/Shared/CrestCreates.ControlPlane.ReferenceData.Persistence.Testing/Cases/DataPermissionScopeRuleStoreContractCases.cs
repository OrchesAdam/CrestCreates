using CrestCreates.Organization.Abstractions;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Runner-free Rule store contract primitives shared by provider runners.
/// </summary>
public static class DataPermissionScopeRuleStoreContractCases
{
    public static async Task RunFrozenSemanticsAsync(IDataPermissionScopeRuleStore store, string prefix)
    {
        async Task Save(string resource, string? action, string? permission, string? tenant, DataPermissionScopeKind scope)
            => await store.SaveRuleAsync(new DataPermissionScopeRule
            {
                Resource = $"{prefix}-{resource}", Action = action, Permission = permission,
                TenantId = tenant, ScopeKind = scope
            });

        await ExactTenantAsync(store, $"{prefix}-p01", "tenant", DataPermissionScopeKind.Self);
        await Save("p02", "read", null, "tenant", DataPermissionScopeKind.OwnOrganization);
        (await store.GetScopeKindAsync($"{prefix}-p02", "read", "other", "tenant")).ShouldBe(DataPermissionScopeKind.OwnOrganization);
        await Save("p03", null, null, "tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync($"{prefix}-p03", "write", "view", "tenant")).ShouldBe(DataPermissionScopeKind.All);
        await Save("p04", "read", "view", null, DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync($"{prefix}-p04", "read", "view", "tenant")).ShouldBe(DataPermissionScopeKind.All);
        await Save("p05", "read", "view", null, DataPermissionScopeKind.All);
        await Save("p05", "read", null, "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync($"{prefix}-p05", "read", "view", "tenant")).ShouldBe(DataPermissionScopeKind.Self);
        await Save("p06", "read", "view", "other-tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync($"{prefix}-p06", "read", "view", "tenant")).ShouldBe(null);
        await Save("p07", "read", "view", "tenant", DataPermissionScopeKind.Self);
        await Save("p07", "read", "view", "tenant", DataPermissionScopeKind.All);
        (await store.GetScopeKindAsync($"{prefix}-p07", "read", "view", "tenant")).ShouldBe(DataPermissionScopeKind.All);

        foreach (var variant in Enum.GetValues<RuleExactEmptyVariant>())
        {
            var resource = $"p10-{variant}";
            switch (variant)
            {
                case RuleExactEmptyVariant.ActionEmpty:
                    await Save(resource, string.Empty, "view", "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync($"{prefix}-{resource}", string.Empty, "view", "tenant")).ShouldBe(DataPermissionScopeKind.Self);
                    break;
                case RuleExactEmptyVariant.PermissionEmpty:
                    await Save(resource, "read", string.Empty, "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync($"{prefix}-{resource}", "read", string.Empty, "tenant")).ShouldBe(DataPermissionScopeKind.Self);
                    break;
                default:
                    await Save(resource, string.Empty, string.Empty, "tenant", DataPermissionScopeKind.Self);
                    await Save(resource, null, null, "tenant", DataPermissionScopeKind.All);
                    (await store.GetScopeKindAsync($"{prefix}-{resource}", string.Empty, string.Empty, "tenant")).ShouldBe(DataPermissionScopeKind.Self);
                    break;
            }
        }

        await Save("p11", null, "view", "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync($"{prefix}-p11", "read", "view", "tenant")).ShouldBe(null);
        await Save("p12", null, "view", "tenant", DataPermissionScopeKind.Self);
        (await store.GetScopeKindAsync($"{prefix}-p12", null, "view", "tenant")).ShouldBe(DataPermissionScopeKind.Self);
    }

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

    private static void ShouldBe(this DataPermissionScopeKind? actual, DataPermissionScopeKind? expected)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Expected scope '{expected}', got '{actual}'.");
    }
}
