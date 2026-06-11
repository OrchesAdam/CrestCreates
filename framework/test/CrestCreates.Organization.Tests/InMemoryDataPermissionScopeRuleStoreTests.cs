using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class InMemoryDataPermissionScopeRuleStoreTests
{
    [Fact]
    public async Task GetScopeKind_MatchesExactRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = "books.read",
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "books.read", "t-A");
        result.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task GetScopeKind_FallsBackToWildcardPermission()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "any.permission", "t-A");
        result.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task GetScopeKind_FallsBackToWildcardActionAndPermission()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = null, Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.Self
        });
        var result = await store.GetScopeKindAsync("Book", "Write", "any.permission", "t-A");
        result.Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task GetScopeKind_ReturnsNull_WhenNoRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetScopeKind_PrefersMoreSpecificRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = null, Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task GetScopeKind_TenantRuleOverridesGlobalRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = null, ScopeKind = DataPermissionScopeKind.Self
        });
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task GetScopeKind_OtherTenantRuleDoesNotApply()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-B");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetScopeKind_TenantWildcardOverridesGlobalExact()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        // Global exact: Book + Read + books.read → All
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = "books.read",
            TenantId = null, ScopeKind = DataPermissionScopeKind.All
        });
        // Tenant wildcard: Book + Read + * → Self
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.Self
        });
        // Tenant wildcard should override global exact
        var result = await store.GetScopeKindAsync("Book", "Read", "books.read", "t-A");
        result.Should().Be(DataPermissionScopeKind.Self);
    }
}
