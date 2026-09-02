using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using Moq;

namespace CrestCreates.Organization.Tests;

public class DataPermissionScopeProviderTests
{
    private static async Task<(
        InMemoryOrganizationStore Store,
        InMemoryDataPermissionScopeRuleStore RuleStore,
        DefaultDataPermissionScopeProvider Provider
    )> CreateProviderAsync(
        List<UserOrganizationMembership>? memberships = null,
        List<OrganizationUnit>? orgUnits = null)
    {
        var store = new InMemoryOrganizationStore();
        if (orgUnits is not null)
            foreach (var u in orgUnits)
                await store.SaveOrganizationUnitAsync(u);
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);

        var identity = new DefaultOrganizationIdentityService(store);
        var hierarchy = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var provider = new DefaultDataPermissionScopeProvider(identity, hierarchy, ruleStore);
        return (store, ruleStore, provider);
    }

    private static DataPermissionScopeRequest Request(string userId,
        string? tenantId = null, string? resource = null,
        string? action = null, string? permission = null)
        => new()
        {
            UserId = userId,
            TenantId = tenantId,
            Resource = resource,
            Action = action,
            Permission = permission
        };

    // D1: No org → Self
    [Fact]
    public async Task GetScope_ReturnsSelf_WhenNoOrganization()
    {
        var (_, _, provider) = await CreateProviderAsync();
        var scope = await provider.GetScopeAsync(Request("user-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().BeNull();
    }

    // D2: Primary org → OwnOrganization
    [Fact]
    public async Task GetScope_ReturnsOwnOrganization_WhenPrimaryExists()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        var scope = await provider.GetScopeAsync(Request("user-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().Be("dept-1");
    }

    // D3: Rule → OwnOrganizationAndDescendants with hierarchy
    [Fact]
    public async Task GetScope_ReturnsOwnOrganizationAndDescendants_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            },
            orgUnits: new()
            {
                new() { Id = "dept-1", Name = "Dept" },
                new() { Id = "team-3", ParentId = "dept-1", Name = "Team 3" },
                new() { Id = "team-4", ParentId = "dept-1", Name = "Team 4" }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.OwnOrganizationAndDescendants });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganizationAndDescendants);
        scope.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1", "team-3", "team-4"]);
    }

    // D4: Rule → All
    [Fact]
    public async Task GetScope_ReturnsAll_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Report", Action = "Read", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Report", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All);
        scope.IsUnrestricted.Should().BeTrue();
        scope.OrganizationUnitId.Should().BeNull();
    }

    // D5: Rule → None
    [Fact]
    public async Task GetScope_ReturnsNone_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync();
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "SecretDoc", Action = "Read", ScopeKind = DataPermissionScopeKind.None });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "SecretDoc", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
        scope.IsEmpty.Should().BeTrue();
    }

    // D6: Rule → OwnOrganization, no primary org → fail closed
    [Fact]
    public async Task GetScope_ReturnsNone_WhenOwnOrganizationWithoutPrimaryOrg()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync();
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Write", ScopeKind = DataPermissionScopeKind.OwnOrganization });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Write"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
    }

    // D7: Rule → OwnOrganizationAndDescendants, no primary org → fail closed
    [Fact]
    public async Task GetScope_ReturnsNone_WhenOwnOrganizationAndDescendantsWithoutPrimaryOrg()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync();
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.OwnOrganizationAndDescendants });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
    }

    // D8: No rule, has org → fallback OwnOrganization
    [Fact]
    public async Task GetScope_FallsBackToOwnOrganization_WhenNoRuleAndHasOrg()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.OrganizationUnitId.Should().Be("dept-1");
    }

    // D9: No rule, no org → fallback Self
    [Fact]
    public async Task GetScope_FallsBackToSelf_WhenNoRuleAndNoOrg()
    {
        var (_, _, provider) = await CreateProviderAsync();

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
    }

    // D10: Rule overrides org membership
    [Fact]
    public async Task GetScope_RuleOverridesOrgMembership()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All);
        scope.OrganizationUnitId.Should().BeNull();
    }

    // D11: Tenant isolation in scope resolution
    [Fact]
    public async Task GetScope_IsTenantAware()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.OrganizationUnitId.Should().Be("dept-1");

        var scope2 = await provider.GetScopeAsync(Request("user-1", tenantId: "t-2"));
        scope2.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope2.OrganizationUnitId.Should().BeNull();
    }

    // D12: Old overload delegates to new
    [Fact]
    public async Task GetScope_OldOverload_DelegatesToNewMethod()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        var newScope = await provider.GetScopeAsync(Request("user-1", permission: "read:docs"));
        var oldScope = await provider.GetScopeAsync("user-1", "read:docs");

        newScope.Kind.Should().Be(oldScope.Kind);
        newScope.OrganizationUnitId.Should().Be(oldScope.OrganizationUnitId);
    }

    // D13: Tenant-specific rule overrides global rule
    [Fact]
    public async Task GetScope_TenantRuleOverridesGlobalRule()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-A", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.Self });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-A", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All);
    }

    // D14: Other tenant rule does not apply
    [Fact]
    public async Task GetScope_OtherTenantRuleDoesNotApply()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-B", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-B", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    // D15: Custom scope rule → None at provider level (fail closed)
    [Fact]
    public async Task GetScope_ReturnsNone_WhenCustomRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.Custom });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
        scope.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task DataPermissionScope_FreshnessFailure_Should_NotReturnExpandedOrganizationScope()
    {
        var store = new InMemoryOrganizationStore();
        await store.SaveMembershipAsync(new UserOrganizationMembership
        {
            Id = "m-1",
            UserId = "user-1",
            TenantId = "tenant-a",
            OrganizationUnitId = "dept-1",
            IsPrimary = true,
            IsActive = true
        });
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book",
            Action = "Read",
            TenantId = "tenant-a",
            ScopeKind = DataPermissionScopeKind.OwnOrganizationAndDescendants
        });
        var hierarchy = new Mock<IOrganizationHierarchyService>();
        hierarchy.Setup(service => service.GetDescendantsAsync(
                "dept-1",
                "tenant-a",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrganizationHierarchyFreshnessException(
                OrganizationHierarchyFreshnessFailureKind.GenerationRegression,
                message: "injected freshness failure"));
        var provider = new DefaultDataPermissionScopeProvider(
            new DefaultOrganizationIdentityService(store),
            hierarchy.Object,
            ruleStore);

        await provider.Invoking(value => value.GetScopeAsync(Request(
                "user-1",
                tenantId: "tenant-a",
                resource: "Book",
                action: "Read")))
            .Should().ThrowAsync<OrganizationHierarchyFreshnessException>()
            .Where(exception => exception.FailureKind == OrganizationHierarchyFreshnessFailureKind.GenerationRegression);
    }
}
