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
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("same", null));
        await driver.Store.SaveOrganizationUnitAsync(Unit("same", "tenant-a"));

        (await driver.Store.GetOrganizationUnitByIdAsync("same"))!.TenantId.Should().BeNull();
        (await driver.Store.GetOrganizationUnitByIdAsync("same", "tenant-a"))!.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task OrganizationIdentitySurface_SameIdInTwoTenants_Should_NotCollide()
    {
        var driver = NewDriver();
        await driver.Store.SavePositionAsync(Position("same", "tenant-a"));
        await driver.Store.SavePositionAsync(Position("same", "tenant-b"));

        (await driver.Store.GetPositionByIdAsync("same", "tenant-a"))!.TenantId.Should().Be("tenant-a");
        (await driver.Store.GetPositionByIdAsync("same", "tenant-b"))!.TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task OrganizationQuerySurface_Should_PreserveExplicitTenantIsolation()
    {
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("a", "tenant-a"));
        await driver.Store.SaveOrganizationUnitAsync(Unit("b", "tenant-b"));

        (await driver.Store.GetOrganizationUnitsAsync("tenant-a"))
            .Select(value => value.Id).Should().Equal("a");
    }

    [Fact]
    public async Task OrganizationQuerySurface_NullTenant_Should_RemainUnfiltered()
    {
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("global", null));
        await driver.Store.SaveOrganizationUnitAsync(Unit("tenant", "tenant-a"));

        (await driver.Store.GetOrganizationUnitsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task OrganizationUnits_Should_OrderBySortOrderScopeThenId()
    {
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
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveMembershipAsync(Membership("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetMembershipsByUserAsync("user", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task MembershipsByUnit_Should_OrderByCreatedAtScopeThenId()
    {
        var driver = NewDriver();
        await driver.Store.SaveMembershipAsync(Membership("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveMembershipAsync(Membership("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetMembershipsByOrganizationUnitAsync("unit", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task RoleAssignments_Should_OrderByCreatedAtScopeThenId()
    {
        var driver = NewDriver();
        await driver.Store.SaveRoleAssignmentAsync(Role("z", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(2)));
        await driver.Store.SaveRoleAssignmentAsync(Role("a", "user", "tenant-a", DateTimeOffset.UnixEpoch.AddTicks(1)));

        (await driver.Store.GetRoleAssignmentsByUserAsync("user", "tenant-a")).Select(value => value.Id)
            .Should().Equal("a", "z");
    }

    [Fact]
    public async Task PrimaryMembership_FullTie_Should_UseNormalizedScopeThenId()
    {
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
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("c", "a:b"));
        await driver.Store.SaveOrganizationUnitAsync(Unit("b:c", "a"));

        (await driver.Store.GetOrganizationUnitByIdAsync("c", "a:b"))!.TenantId.Should().Be("a:b");
        (await driver.Store.GetOrganizationUnitByIdAsync("b:c", "a"))!.TenantId.Should().Be("a");
    }

    [Fact]
    public async Task OrganizationEntitySurface_Save_Should_CaptureSnapshot()
    {
        var driver = NewDriver();
        var unit = Unit("unit", "tenant-a", sortOrder: 7);
        await driver.Store.SaveOrganizationUnitAsync(unit);

        (await driver.Store.GetOrganizationUnitByIdAsync(unit.Id, unit.TenantId))
            .Should().BeEquivalentTo(unit);
    }

    [Fact]
    public async Task OrganizationReadSurface_Should_ReturnDetachedSnapshot()
    {
        var driver = NewDriver();
        await driver.Store.SaveOrganizationUnitAsync(Unit("unit", "tenant-a"));

        var first = await driver.Store.GetOrganizationUnitByIdAsync("unit", "tenant-a");
        var second = await driver.Store.GetOrganizationUnitByIdAsync("unit", "tenant-a");
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task OrganizationCreatedAtVariant_Should_PreserveExactOrderAndSnapshot()
    {
        var driver = NewDriver();
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));
        var second = first.AddTicks(1);
        await driver.Store.SaveMembershipAsync(Membership("first", "user", "tenant-a", first));
        await driver.Store.SaveMembershipAsync(Membership("second", "user", "tenant-a", second));

        var result = await driver.Store.GetMembershipsByUserAsync("user", "tenant-a");
        result.Select(value => value.Id).Should().Equal("first", "second");
        result[0].CreatedAt.Should().Be(first);
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
