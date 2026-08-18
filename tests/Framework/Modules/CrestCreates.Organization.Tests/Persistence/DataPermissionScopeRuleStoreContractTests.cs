using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
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

    [Theory]
    [InlineData(IdentityValidationVector.RuleNullInstance)]
    [InlineData(IdentityValidationVector.RuleInvalidResource)]
    [InlineData(IdentityValidationVector.RuleInvalidNonNullTenant)]
    public async Task IdentityValidationVector_Should_FailBeforeMutation(IdentityValidationVector variant)
    {
        var driver = NewDriver();
        Func<Task> act = variant switch
        {
            IdentityValidationVector.RuleNullInstance =>
                () => driver.Store.SaveRuleAsync(null!),
            IdentityValidationVector.RuleInvalidResource =>
                () => driver.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "", ScopeKind = DataPermissionScopeKind.Self }),
            IdentityValidationVector.RuleInvalidNonNullTenant =>
                () => driver.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "resource", TenantId = "  ", ScopeKind = DataPermissionScopeKind.Self }),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(RuleSentinelField.Action)]
    [InlineData(RuleSentinelField.Permission)]
    [InlineData(RuleSentinelField.TenantId)]
    public async Task RuleSentinelField_Should_FailBeforeMutation(RuleSentinelField field)
    {
        var driver = NewDriver();
        var rule = field switch
        {
            RuleSentinelField.Action => new DataPermissionScopeRule
                { Resource = "resource", Action = "*", ScopeKind = DataPermissionScopeKind.Self },
            RuleSentinelField.Permission => new DataPermissionScopeRule
                { Resource = "resource", Permission = "*", ScopeKind = DataPermissionScopeKind.Self },
            RuleSentinelField.TenantId => new DataPermissionScopeRule
                { Resource = "resource", TenantId = "*", ScopeKind = DataPermissionScopeKind.Self },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        await ((Func<Task>)(() => driver.Store.SaveRuleAsync(rule))).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(PersistedEnumSurface.RuleScopeKind)]
    public async Task PersistedEnumSurface_Should_FailBeforeMutation(PersistedEnumSurface surface)
    {
        var driver = NewDriver();
        var rule = new DataPermissionScopeRule
        {
            Resource = "resource",
            ScopeKind = (DataPermissionScopeKind)999
        };
        await ((Func<Task>)(() => driver.Store.SaveRuleAsync(rule))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(StoreMethodSurface.RuleSave)]
    [InlineData(StoreMethodSurface.RuleGet)]
    public async Task PreCancelledStoreMethod_Should_ExitBeforeQueryOrMutation(StoreMethodSurface surface)
    {
        var driver = NewDriver();
        var ct = new CancellationToken(canceled: true);
        Func<Task> act = surface switch
        {
            StoreMethodSurface.RuleSave =>
                () => driver.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "r", ScopeKind = DataPermissionScopeKind.Self }, ct),
            StoreMethodSurface.RuleGet =>
                async () => await driver.Store.GetScopeKindAsync("resource", null, null, cancellationToken: ct),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
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
