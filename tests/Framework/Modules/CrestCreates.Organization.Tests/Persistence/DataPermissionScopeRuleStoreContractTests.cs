using CrestCreates.Organization.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Organization.Tests.Persistence;

public sealed class DataPermissionScopeRuleStoreContractTests
{
    [Fact]
    public async Task DataPermissionRule_Should_MatchTenantExact()
    {
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task DataPermissionRule_Should_MatchTenantWildcardPermission()
    {
        var driver = NewDriver();
        await Save(driver, "read", null, "tenant-a", DataPermissionScopeKind.OwnOrganization);
        (await driver.Store.GetScopeKindAsync("resource", "read", "other", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task DataPermissionRule_Should_MatchTenantWildcardAction()
    {
        var driver = NewDriver();
        await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "write", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_Should_FallBackToGlobal()
    {
        var driver = NewDriver();
        await Save(driver, "read", "view", null, DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_TenantWildcard_Should_WinOverGlobalExact()
    {
        var driver = NewDriver();
        await Save(driver, "read", "view", null, DataPermissionScopeKind.All);
        await Save(driver, "read", null, "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task DataPermissionRule_OtherTenant_Should_NotApply()
    {
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-b"))
            .Should().BeNull();
    }

    [Fact]
    public async Task DataPermissionRule_Save_Should_ReplaceExactRule()
    {
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.Self);
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_EmptyExact_Should_RemainDistinctFromWildcard()
    {
        var driver = NewDriver();
        await Save(driver, string.Empty, string.Empty, "tenant-a", DataPermissionScopeKind.Self);
        await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);

        (await driver.Store.GetScopeKindAsync("resource", string.Empty, string.Empty, "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_WildcardActionExactPermission_Should_NotMatchNonNullAction()
    {
        var driver = NewDriver();
        await Save(driver, null, "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().BeNull();
    }

    [Fact]
    public async Task DataPermissionRule_WildcardActionExactPermission_Should_MatchNullActionRequest()
    {
        var driver = NewDriver();
        await Save(driver, null, "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", null, "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    private static InMemoryDataPermissionScopeRuleStoreContractDriver NewDriver() => new();

    private static Task Save(
        InMemoryDataPermissionScopeRuleStoreContractDriver driver,
        string? action,
        string? permission,
        string? tenantId,
        DataPermissionScopeKind scopeKind)
        => driver.Store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "resource",
            Action = action,
            Permission = permission,
            TenantId = tenantId,
            ScopeKind = scopeKind
        });
}
