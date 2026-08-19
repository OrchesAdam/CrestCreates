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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P01, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task DataPermissionRule_Should_MatchTenantWildcardPermission()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P02, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", null, "tenant-a", DataPermissionScopeKind.OwnOrganization);
        (await driver.Store.GetScopeKindAsync("resource", "read", "other", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task DataPermissionRule_Should_MatchTenantWildcardAction()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P03, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "write", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_Should_FallBackToGlobal()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P04, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", "view", null, DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task DataPermissionRule_TenantWildcard_Should_WinOverGlobalExact()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P05, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", "view", null, DataPermissionScopeKind.All);
        await Save(driver, "read", null, "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task DataPermissionRule_OtherTenant_Should_NotApply()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P06, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-b"))
            .Should().BeNull();
    }

    [Fact]
    public async Task DataPermissionRule_Save_Should_ReplaceExactRule()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P07, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.Self);
        await Save(driver, "read", "view", "tenant-a", DataPermissionScopeKind.All);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.All);
    }

    [Theory]
    [InlineData(RuleExactEmptyVariant.ActionEmpty)]
    [InlineData(RuleExactEmptyVariant.PermissionEmpty)]
    [InlineData(RuleExactEmptyVariant.BothEmpty)]
    public async Task DataPermissionRule_EmptyExact_Should_RemainDistinctFromWildcard(RuleExactEmptyVariant variant)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P10, "Rule", variant.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        switch (variant)
        {
            case RuleExactEmptyVariant.ActionEmpty:
                await Save(driver, string.Empty, "view", "tenant-a", DataPermissionScopeKind.Self);
                await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);
                (await driver.Store.GetScopeKindAsync("resource", string.Empty, "view", "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.Self);
                (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.All);
                break;
            case RuleExactEmptyVariant.PermissionEmpty:
                await Save(driver, "read", string.Empty, "tenant-a", DataPermissionScopeKind.Self);
                await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);
                (await driver.Store.GetScopeKindAsync("resource", "read", string.Empty, "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.Self);
                (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.All);
                break;
            default:
                await Save(driver, string.Empty, string.Empty, "tenant-a", DataPermissionScopeKind.Self);
                await Save(driver, null, null, "tenant-a", DataPermissionScopeKind.All);

                (await driver.Store.GetScopeKindAsync("resource", string.Empty, string.Empty, "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.Self);
                (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
                    .Should().Be(DataPermissionScopeKind.All);
                break;
        }
    }

    [Fact]
    public async Task DataPermissionRule_WildcardActionExactPermission_Should_NotMatchNonNullAction()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P11, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, null, "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", "read", "view", "tenant-a"))
            .Should().BeNull();
    }

    [Fact]
    public async Task DataPermissionRule_WildcardActionExactPermission_Should_MatchNullActionRequest()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.P12, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await Save(driver, null, "view", "tenant-a", DataPermissionScopeKind.Self);
        (await driver.Store.GetScopeKindAsync("resource", null, "view", "tenant-a"))
            .Should().Be(DataPermissionScopeKind.Self);
    }

    [Theory]
    [InlineData(IdentityValidationVector.RuleNullInstance, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.RuleInvalidResource, EvidenceVectorKey.Null)]
    [InlineData(IdentityValidationVector.RuleInvalidResource, EvidenceVectorKey.Empty)]
    [InlineData(IdentityValidationVector.RuleInvalidNonNullTenant, EvidenceVectorKey.Empty)]
    [InlineData(IdentityValidationVector.RuleInvalidNonNullTenant, EvidenceVectorKey.Whitespace)]
    public async Task IdentityValidationVector_Should_FailBeforeMutation(IdentityValidationVector variant, EvidenceVectorKey key)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V01, "Validation", variant.ToString(), key, RequiredRunner.InMemory);
        var driver = NewDriver();
        Func<Task> act = variant switch
        {
            IdentityValidationVector.RuleNullInstance =>
                () => driver.Store.SaveRuleAsync(null!),
            IdentityValidationVector.RuleInvalidResource =>
                () => driver.Store.SaveRuleAsync(new DataPermissionScopeRule
                {
                    Resource = InvalidText(key)!,
                    ScopeKind = DataPermissionScopeKind.Self
                }),
            IdentityValidationVector.RuleInvalidNonNullTenant =>
                () => driver.Store.SaveRuleAsync(new DataPermissionScopeRule
                {
                    Resource = "resource",
                    TenantId = InvalidText(key),
                    ScopeKind = DataPermissionScopeKind.Self
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static string? InvalidText(EvidenceVectorKey key)
        => key switch
        {
            EvidenceVectorKey.Null => null,
            EvidenceVectorKey.Empty => string.Empty,
            EvidenceVectorKey.Whitespace => "   ",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported invalid-value vector")
        };

    [Theory]
    [InlineData(RuleSentinelField.Action)]
    [InlineData(RuleSentinelField.Permission)]
    [InlineData(RuleSentinelField.TenantId)]
    public async Task RuleSentinelField_Should_FailBeforeMutation(RuleSentinelField field)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V02, "Validation", field.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V03, "Validation", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V05, "Validation", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
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
