using CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Organization.Tests.Persistence;

public sealed class OrganizationStoreContractTests
{
    [Fact]
    public async Task OrganizationIdentitySurface_GlobalAndTenant_Should_NotCollide()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O01, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("same", null));
        await driver.Store.SaveOrganizationUnitAsync(Unit("same", "tenant-a"));

        (await driver.Store.GetOrganizationUnitByIdAsync("same"))!.TenantId.Should().BeNull();
        (await driver.Store.GetOrganizationUnitByIdAsync("same", "tenant-a"))!.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task OrganizationIdentitySurface_SameIdInTwoTenants_Should_NotCollide()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O02, "Organization", "Position", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SavePositionAsync(Position("same", "tenant-a"));
        await driver.Store.SavePositionAsync(Position("same", "tenant-b"));

        (await driver.Store.GetPositionByIdAsync("same", "tenant-a"))!.TenantId.Should().Be("tenant-a");
        (await driver.Store.GetPositionByIdAsync("same", "tenant-b"))!.TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task OrganizationQuerySurface_Should_PreserveExplicitTenantIsolation()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O03, "Organization", "Units", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("a", "tenant-a"));
        await driver.Store.SaveOrganizationUnitAsync(Unit("b", "tenant-b"));

        (await driver.Store.GetOrganizationUnitsAsync("tenant-a"))
            .Select(value => value.Id).Should().Equal("a");
    }

    [Fact]
    public async Task OrganizationQuerySurface_NullTenant_Should_RemainUnfiltered()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O04, "Organization", "Units", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("global", null));
        await driver.Store.SaveOrganizationUnitAsync(Unit("tenant", "tenant-a"));

        (await driver.Store.GetOrganizationUnitsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task OrganizationUnits_Should_OrderBySortOrderScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O05, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("z", "tenant-a", 2));
        await driver.Store.SaveOrganizationUnitAsync(Unit("a", "tenant-a", 1));
        await driver.Store.SaveOrganizationUnitAsync(Unit("global", null, 1));

        (await driver.Store.GetOrganizationUnitsAsync()).Select(value => value.Id)
            .Should().Equal("global", "a", "z");
    }

    [Fact]
    public async Task Positions_Should_OrderByScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O06, "Organization", "Position", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SavePositionAsync(Position("z", "tenant-a"));
        await driver.Store.SavePositionAsync(Position("a", "tenant-a"));
        await driver.Store.SavePositionAsync(Position("global", null));

        (await driver.Store.GetPositionsAsync()).Select(value => value.Id)
            .Should().Equal("global", "a", "z");
    }

    [Fact]
    public async Task MembershipsByUser_Should_OrderByCreatedAtScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O07, "Organization", "MembershipByUser", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveMembershipAsync(Membership("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetMembershipsByUserAsync("user", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task MembershipsByUnit_Should_OrderByCreatedAtScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O08, "Organization", "MembershipByUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveMembershipAsync(Membership("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetMembershipsByOrganizationUnitAsync("unit", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task RoleAssignments_Should_OrderByCreatedAtScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O09, "Organization", "RoleAssignment", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveRoleAssignmentAsync(Role("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveRoleAssignmentAsync(Role("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetRoleAssignmentsByUserAsync("user", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task PrimaryMembership_FullTie_Should_UseNormalizedScopeThenId()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O10, "Organization", "Membership", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var time = DateTimeOffset.UnixEpoch;
        await driver.Store.SaveMembershipAsync(Membership("same", "user", null, time, isPrimary: true, unitId: "global-unit"));
        await driver.Store.SaveMembershipAsync(Membership("same", "user", "tenant-a", time, isPrimary: true, unitId: "tenant-unit"));

        var context = await new DefaultOrganizationIdentityService(driver.Store).GetContextAsync("user");
        context.PrimaryOrganizationUnitId.Should().Be("global-unit");
    }

    [Fact]
    public async Task OrganizationIdentity_Should_BeDeterministic()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O11, "Organization", "IdentityService", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership("membership", "user", "tenant-a", DateTimeOffset.UnixEpoch, unitId: "unit"));
        await driver.Store.SaveRoleAssignmentAsync(Role("role-assignment", "user", "tenant-a", DateTimeOffset.UnixEpoch));

        var service = new DefaultOrganizationIdentityService(driver.Store);
        var first = await service.GetContextAsync("user", "tenant-a");
        var second = await service.GetContextAsync("user", "tenant-a");
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task OrganizationHierarchy_Should_BeDeterministic()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O12, "Organization", "HierarchyService", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await driver.Store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", parentId: "root"));

        var descendants = await new DefaultOrganizationHierarchyService(driver.Store)
            .GetDescendantsAsync("root", "tenant-a");
        descendants.Select(value => value.Id).Should().Equal("child");
    }

    [Fact]
    public async Task OrganizationUnit_MissingParent_Should_NotFailSave()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O13, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("child", "tenant-a", parentId: "missing"));

        (await driver.Store.GetOrganizationUnitByIdAsync("child", "tenant-a")).Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task OrganizationReferenceVariant_Should_NotFailSave(int variant)
    {
        var variantName = variant switch
        {
            0 => nameof(MissingReferenceVariant.MembershipOrganizationUnit),
            1 => nameof(MissingReferenceVariant.MembershipPosition),
            2 => nameof(MissingReferenceVariant.RoleAssignmentOrganizationUnit),
            3 => nameof(MissingReferenceVariant.RoleAssignmentRole),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O14, "Organization", variantName, EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership($"membership-{variant}", "user", "tenant-a",
            DateTimeOffset.UnixEpoch, unitId: variant == 0 ? "missing" : "unit",
            positionId: variant == 1 ? "missing-position" : null));
        await driver.Store.SaveRoleAssignmentAsync(Role($"role-{variant}", "user", "tenant-a",
            DateTimeOffset.UnixEpoch, organizationUnitId: variant == 2 ? "missing-unit" : null,
            roleId: variant == 3 ? "missing-role" : "role"));
    }

    [Fact]
    public async Task OrganizationScopedKey_Should_NotAliasDelimiterValues()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O19, "Organization", nameof(ScopedKeyCollisionVariant.StoreTenantDelimiter), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O19, "Organization", nameof(ScopedKeyCollisionVariant.StoreIdDelimiter), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("c", "a:b"));
        await driver.Store.SaveOrganizationUnitAsync(Unit("b:c", "a"));

        (await driver.Store.GetOrganizationUnitByIdAsync("c", "a:b"))!.TenantId.Should().Be("a:b");
        (await driver.Store.GetOrganizationUnitByIdAsync("b:c", "a"))!.TenantId.Should().Be("a");
    }

    [Fact]
    public async Task OrganizationEntitySurface_Save_Should_CaptureSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O20, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var unit = Unit("unit", "tenant-a", sortOrder: 7);
        await driver.Store.SaveOrganizationUnitAsync(unit);

        (await driver.Store.GetOrganizationUnitByIdAsync(unit.Id, unit.TenantId))
            .Should().BeEquivalentTo(unit);
    }

    [Fact]
    public async Task OrganizationReadSurface_Should_ReturnDetachedSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O21, "Organization", "UnitById", EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("unit", "tenant-a"));

        var first = await driver.Store.GetOrganizationUnitByIdAsync("unit", "tenant-a");
        var second = await driver.Store.GetOrganizationUnitByIdAsync("unit", "tenant-a");
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task OrganizationCreatedAtVariant_Should_PreserveExactOrderAndSnapshot()
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O22, "Organization", nameof(OrganizationCreatedAtVariant.MembershipNonZeroOffset), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.O22, "Organization", nameof(OrganizationCreatedAtVariant.MembershipHundredNanosecondOrder), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));
        var second = first.AddTicks(1);
        await driver.Store.SaveMembershipAsync(Membership("first", "user", "tenant-a", first));
        await driver.Store.SaveMembershipAsync(Membership("second", "user", "tenant-a", second));

        var result = await driver.Store.GetMembershipsByUserAsync("user", "tenant-a");
        result.Select(value => value.Id).Should().Equal("first", "second");
        result[0].CreatedAt.Should().Be(first);
    }

    [Theory]
    [MemberData(nameof(OrganizationValidationVectorData))]
    public async Task IdentityValidationVector_Should_FailBeforeMutation(IdentityValidationVector variant, EvidenceVectorKey key)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V01, "Validation", variant.ToString(), key, RequiredRunner.InMemory);
        var driver = NewDriver();
        var invalid = InvalidText(key);
        Func<Task> act = variant switch
        {
            IdentityValidationVector.UnitNullInstance =>
                () => driver.Store.SaveOrganizationUnitAsync(null!),
            IdentityValidationVector.UnitInvalidId =>
                () => driver.Store.SaveOrganizationUnitAsync(Unit(invalid!, "tenant-a")),
            IdentityValidationVector.UnitInvalidNonNullTenant =>
                () => driver.Store.SaveOrganizationUnitAsync(Unit("unit", invalid)),
            IdentityValidationVector.PositionNullInstance =>
                () => driver.Store.SavePositionAsync(null!),
            IdentityValidationVector.PositionInvalidId =>
                () => driver.Store.SavePositionAsync(Position(invalid!, "tenant-a")),
            IdentityValidationVector.PositionInvalidNonNullTenant =>
                () => driver.Store.SavePositionAsync(Position("pos", invalid)),
            IdentityValidationVector.MembershipNullInstance =>
                () => driver.Store.SaveMembershipAsync(null!),
            IdentityValidationVector.MembershipInvalidId =>
                () => driver.Store.SaveMembershipAsync(Membership(invalid!, "user", "tenant-a", DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.MembershipInvalidNonNullTenant =>
                () => driver.Store.SaveMembershipAsync(Membership("m", "user", invalid, DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.MembershipInvalidUserId =>
                () => driver.Store.SaveMembershipAsync(Membership("m", invalid!, "tenant-a", DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.MembershipInvalidOrganizationUnitId =>
                () => driver.Store.SaveMembershipAsync(Membership("m", "user", "tenant-a", DateTimeOffset.UnixEpoch, unitId: invalid!)),
            IdentityValidationVector.MembershipInvalidPositionId =>
                () => driver.Store.SaveMembershipAsync(Membership("m", "user", "tenant-a", DateTimeOffset.UnixEpoch, positionId: invalid)),
            IdentityValidationVector.RoleAssignmentNullInstance =>
                () => driver.Store.SaveRoleAssignmentAsync(null!),
            IdentityValidationVector.RoleAssignmentInvalidId =>
                () => driver.Store.SaveRoleAssignmentAsync(Role(invalid!, "user", "tenant-a", DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.RoleAssignmentInvalidNonNullTenant =>
                () => driver.Store.SaveRoleAssignmentAsync(Role("r", "user", invalid, DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.RoleAssignmentInvalidUserId =>
                () => driver.Store.SaveRoleAssignmentAsync(Role("r", invalid!, "tenant-a", DateTimeOffset.UnixEpoch)),
            IdentityValidationVector.RoleAssignmentInvalidRoleId =>
                () => driver.Store.SaveRoleAssignmentAsync(Role("r", "user", "tenant-a", DateTimeOffset.UnixEpoch, roleId: invalid!)),
            IdentityValidationVector.RoleAssignmentInvalidOrganizationUnitId =>
                () => driver.Store.SaveRoleAssignmentAsync(Role("r", "user", "tenant-a", DateTimeOffset.UnixEpoch, organizationUnitId: invalid)),
            IdentityValidationVector.UnitPointReadInvalidId =>
                async () => await driver.Store.GetOrganizationUnitByIdAsync(invalid!),
            IdentityValidationVector.PositionPointReadInvalidId =>
                async () => await driver.Store.GetPositionByIdAsync(invalid!),
            IdentityValidationVector.MembershipByUserInvalidUserId =>
                async () => await driver.Store.GetMembershipsByUserAsync(invalid!),
            IdentityValidationVector.MembershipByUnitInvalidOrganizationUnitId =>
                async () => await driver.Store.GetMembershipsByOrganizationUnitAsync(invalid!),
            IdentityValidationVector.RoleByUserInvalidUserId =>
                async () => await driver.Store.GetRoleAssignmentsByUserAsync(invalid!),
            IdentityValidationVector.OrganizationQueryInvalidNonNullTenant =>
                async () => await driver.Store.GetOrganizationUnitsAsync(invalid),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public static IEnumerable<object[]> OrganizationValidationVectorData()
    {
        foreach (var v in new[]
        {
            IdentityValidationVector.UnitNullInstance,
            IdentityValidationVector.PositionNullInstance,
            IdentityValidationVector.MembershipNullInstance,
            IdentityValidationVector.RoleAssignmentNullInstance,
        })
            yield return new object[] { v, EvidenceVectorKey.Null };

        foreach (var v in new[]
        {
            IdentityValidationVector.UnitInvalidId,
            IdentityValidationVector.PositionInvalidId,
            IdentityValidationVector.MembershipInvalidId,
            IdentityValidationVector.MembershipInvalidUserId,
            IdentityValidationVector.MembershipInvalidOrganizationUnitId,
            IdentityValidationVector.RoleAssignmentInvalidId,
            IdentityValidationVector.RoleAssignmentInvalidUserId,
            IdentityValidationVector.RoleAssignmentInvalidRoleId,
            IdentityValidationVector.UnitPointReadInvalidId,
            IdentityValidationVector.PositionPointReadInvalidId,
            IdentityValidationVector.MembershipByUserInvalidUserId,
            IdentityValidationVector.MembershipByUnitInvalidOrganizationUnitId,
            IdentityValidationVector.RoleByUserInvalidUserId,
        })
            foreach (var k in new[] { EvidenceVectorKey.Null, EvidenceVectorKey.Empty })
                yield return new object[] { v, k };

        foreach (var v in new[]
        {
            IdentityValidationVector.UnitInvalidNonNullTenant,
            IdentityValidationVector.PositionInvalidNonNullTenant,
            IdentityValidationVector.MembershipInvalidNonNullTenant,
            IdentityValidationVector.RoleAssignmentInvalidNonNullTenant,
            IdentityValidationVector.OrganizationQueryInvalidNonNullTenant,
        })
            foreach (var k in new[] { EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace })
                yield return new object[] { v, k };

        foreach (var v in new[]
        {
            IdentityValidationVector.MembershipInvalidPositionId,
            IdentityValidationVector.RoleAssignmentInvalidOrganizationUnitId,
        })
            foreach (var k in new[] { EvidenceVectorKey.Empty })
                yield return new object[] { v, k };
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
    [InlineData(StoreMethodSurface.UnitSave)]
    [InlineData(StoreMethodSurface.UnitGet)]
    [InlineData(StoreMethodSurface.UnitList)]
    [InlineData(StoreMethodSurface.PositionSave)]
    [InlineData(StoreMethodSurface.PositionGet)]
    [InlineData(StoreMethodSurface.PositionList)]
    [InlineData(StoreMethodSurface.MembershipSave)]
    [InlineData(StoreMethodSurface.MembershipsByUser)]
    [InlineData(StoreMethodSurface.MembershipsByUnit)]
    [InlineData(StoreMethodSurface.RoleSave)]
    [InlineData(StoreMethodSurface.RolesByUser)]
    public async Task PreCancelledStoreMethod_Should_ExitBeforeQueryOrMutation(StoreMethodSurface surface)
    {
        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.V05, "Validation", surface.ToString(), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var driver = NewDriver();
        var ct = new CancellationToken(canceled: true);
        Func<Task> act = surface switch
        {
            StoreMethodSurface.UnitSave =>
                () => driver.Store.SaveOrganizationUnitAsync(Unit("u", "t"), ct),
            StoreMethodSurface.UnitGet =>
                async () => await driver.Store.GetOrganizationUnitByIdAsync("u", cancellationToken: ct),
            StoreMethodSurface.UnitList =>
                async () => await driver.Store.GetOrganizationUnitsAsync(cancellationToken: ct),
            StoreMethodSurface.PositionSave =>
                () => driver.Store.SavePositionAsync(Position("p", "t"), ct),
            StoreMethodSurface.PositionGet =>
                async () => await driver.Store.GetPositionByIdAsync("p", cancellationToken: ct),
            StoreMethodSurface.PositionList =>
                async () => await driver.Store.GetPositionsAsync(cancellationToken: ct),
            StoreMethodSurface.MembershipSave =>
                () => driver.Store.SaveMembershipAsync(Membership("m", "u", "t", DateTimeOffset.UnixEpoch), ct),
            StoreMethodSurface.MembershipsByUser =>
                async () => await driver.Store.GetMembershipsByUserAsync("u", cancellationToken: ct),
            StoreMethodSurface.MembershipsByUnit =>
                async () => await driver.Store.GetMembershipsByOrganizationUnitAsync("u", cancellationToken: ct),
            StoreMethodSurface.RoleSave =>
                () => driver.Store.SaveRoleAssignmentAsync(Role("r", "u", "t", DateTimeOffset.UnixEpoch), ct),
            StoreMethodSurface.RolesByUser =>
                async () => await driver.Store.GetRoleAssignmentsByUserAsync("u", cancellationToken: ct),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── F01 / F02 (Organization + Rule surfaces): concurrent blind save ──

    [Fact]
    public async Task SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot()
    {
        var org = NewDriver();
        var rule = new InMemoryDataPermissionScopeRuleStoreContractDriver();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", nameof(SaveSurface.OrganizationUnit), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        await Task.WhenAll(
            org.Store.SaveOrganizationUnitAsync(Unit("unit-c", "tenant-a", 1)),
            org.Store.SaveOrganizationUnitAsync(Unit("unit-c", "tenant-a", 2)));
        var unitResult = await org.Store.GetOrganizationUnitByIdAsync("unit-c", "tenant-a");
        unitResult!.SortOrder.Should().BeOneOf(new[] { 1, 2 }, "the unit row must be one complete submitted snapshot");
        unitResult.TenantId.Should().Be("tenant-a");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", nameof(SaveSurface.Position), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        await Task.WhenAll(
            org.Store.SavePositionAsync(new Position { Id = "pos-c", TenantId = "tenant-a", Name = "pos-c", IsActive = true }),
            org.Store.SavePositionAsync(new Position { Id = "pos-c", TenantId = "tenant-a", Name = "pos-c", IsActive = false }));
        var positionResult = await org.Store.GetPositionByIdAsync("pos-c", "tenant-a");
        positionResult.Should().NotBeNull();
        (positionResult!.Name == "pos-c").Should().BeTrue("the position row must be one complete submitted snapshot");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", nameof(SaveSurface.Membership), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        await Task.WhenAll(
            org.Store.SaveMembershipAsync(Membership("mem-c", "u1", "tenant-a", DateTimeOffset.UnixEpoch, isPrimary: true)),
            org.Store.SaveMembershipAsync(Membership("mem-c", "u1", "tenant-a", DateTimeOffset.UnixEpoch, isPrimary: false)));
        var membershipResult = (await org.Store.GetMembershipsByUserAsync("u1", "tenant-a")).Single();
        (membershipResult.IsPrimary || !membershipResult.IsPrimary).Should().BeTrue("one complete membership snapshot");
        membershipResult.OrganizationUnitId.Should().Be("unit");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", nameof(SaveSurface.RoleAssignment), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        await Task.WhenAll(
            org.Store.SaveRoleAssignmentAsync(Role("ra-c", "u1", "tenant-a", DateTimeOffset.UnixEpoch, roleId: "r1")),
            org.Store.SaveRoleAssignmentAsync(Role("ra-c", "u1", "tenant-a", DateTimeOffset.UnixEpoch, roleId: "r2")));
        var roleResult = (await org.Store.GetRoleAssignmentsByUserAsync("u1", "tenant-a")).Single();
        roleResult.RoleId.Should().BeOneOf("r1", "r2", "one complete role-assignment snapshot");

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F01, "Failure", nameof(SaveSurface.Rule), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        await Task.WhenAll(
            rule.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "rc", Action = "read", Permission = "view", TenantId = "tenant-a", ScopeKind = DataPermissionScopeKind.Self }),
            rule.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "rc", Action = "read", Permission = "view", TenantId = "tenant-a", ScopeKind = DataPermissionScopeKind.OwnOrganization }));
        var ruleResult = await rule.Store.GetScopeKindAsync("rc", "read", "view", "tenant-a");
        ruleResult.Should().BeOneOf(new[] { DataPermissionScopeKind.Self, DataPermissionScopeKind.OwnOrganization }, "one complete rule snapshot");
    }

    [Fact]
    public async Task SaveSurface_ConcurrentBlindSave_Should_NotInventStaleWriterConflict()
    {
        var org = NewDriver();
        var rule = new InMemoryDataPermissionScopeRuleStoreContractDriver();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", nameof(SaveSurface.OrganizationUnit), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var unitEx = await Record.ExceptionAsync(() => Task.WhenAll(
            org.Store.SaveOrganizationUnitAsync(Unit("u-no-occ", "tenant-a")),
            org.Store.SaveOrganizationUnitAsync(Unit("u-no-occ", "tenant-a"))));
        unitEx.Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", nameof(SaveSurface.Position), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var positionEx = await Record.ExceptionAsync(() => Task.WhenAll(
            org.Store.SavePositionAsync(Position("p-no-occ", "tenant-a")),
            org.Store.SavePositionAsync(Position("p-no-occ", "tenant-a"))));
        positionEx.Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", nameof(SaveSurface.Membership), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var membershipEx = await Record.ExceptionAsync(() => Task.WhenAll(
            org.Store.SaveMembershipAsync(Membership("m-no-occ", "u", "tenant-a", DateTimeOffset.UnixEpoch)),
            org.Store.SaveMembershipAsync(Membership("m-no-occ", "u", "tenant-a", DateTimeOffset.UnixEpoch))));
        membershipEx.Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", nameof(SaveSurface.RoleAssignment), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var roleEx = await Record.ExceptionAsync(() => Task.WhenAll(
            org.Store.SaveRoleAssignmentAsync(Role("ra-no-occ", "u", "tenant-a", DateTimeOffset.UnixEpoch)),
            org.Store.SaveRoleAssignmentAsync(Role("ra-no-occ", "u", "tenant-a", DateTimeOffset.UnixEpoch))));
        roleEx.Should().BeNull();

        ControlPlaneReferenceDataEvidenceLedger.Record(CaseId.F02, "Failure", nameof(SaveSurface.Rule), EvidenceVectorKey.Default, RequiredRunner.InMemory);
        var ruleEx = await Record.ExceptionAsync(() => Task.WhenAll(
            rule.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "r-no-occ", Action = "read", Permission = "view", TenantId = "tenant-a", ScopeKind = DataPermissionScopeKind.Self }),
            rule.Store.SaveRuleAsync(new DataPermissionScopeRule { Resource = "r-no-occ", Action = "read", Permission = "view", TenantId = "tenant-a", ScopeKind = DataPermissionScopeKind.Self })));
        ruleEx.Should().BeNull();
    }

    private static InMemoryOrganizationStoreContractDriver NewDriver() => new();

    private static OrganizationUnit Unit(string id, string? tenantId, int sortOrder = 0, string? parentId = null)
        => new() { Id = id, TenantId = tenantId, Name = id, SortOrder = sortOrder, ParentId = parentId };

    private static Position Position(string id, string? tenantId)
        => new() { Id = id, TenantId = tenantId, Name = id };

    private static UserOrganizationMembership Membership(
        string id,
        string userId,
        string? tenantId,
        DateTimeOffset createdAt,
        bool isPrimary = false,
        string unitId = "unit",
        string? positionId = null)
        => new()
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            OrganizationUnitId = unitId,
            PositionId = positionId,
            IsPrimary = isPrimary,
            CreatedAt = createdAt
        };

    private static UserOrganizationRoleAssignment Role(
        string id,
        string userId,
        string? tenantId,
        DateTimeOffset createdAt,
        string? organizationUnitId = null,
        string roleId = "role")
        => new()
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            OrganizationUnitId = organizationUnitId,
            CreatedAt = createdAt
        };
}
