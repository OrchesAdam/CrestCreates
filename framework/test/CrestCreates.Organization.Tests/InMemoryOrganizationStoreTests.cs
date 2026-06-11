using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class InMemoryOrganizationStoreTests
{
    private readonly InMemoryOrganizationStore _store = new();

    [Fact]
    public async Task SaveOrganizationUnit_And_GetById_Returns_UpsertedUnit()
    {
        var unit = new OrganizationUnit { Id = "dept-1", Name = "Engineering", Code = "ENG" };
        await _store.SaveOrganizationUnitAsync(unit);
        var result = await _store.GetOrganizationUnitByIdAsync("dept-1");
        result.Should().NotBeNull();
        result!.Id.Should().Be("dept-1");
        result.Name.Should().Be("Engineering");
        result.Code.Should().Be("ENG");
        result.Should().NotBeSameAs(unit);
    }

    [Fact]
    public async Task SaveOrganizationUnit_Upserts_ExistingUnit()
    {
        var unit = new OrganizationUnit { Id = "dept-1", Name = "Engineering" };
        await _store.SaveOrganizationUnitAsync(unit);
        var updated = new OrganizationUnit { Id = "dept-1", Name = "Engineering V2" };
        await _store.SaveOrganizationUnitAsync(updated);
        var result = await _store.GetOrganizationUnitByIdAsync("dept-1");
        result!.Name.Should().Be("Engineering V2");
    }

    [Fact]
    public async Task GetOrganizationUnitById_ReturnsNull_WhenNotFound()
    {
        var result = await _store.GetOrganizationUnitByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrganizationUnits_ReturnsAll_WhenNoTenantFilter()
    {
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-1", Name = "Eng", TenantId = "t1" });
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-2", Name = "HR", TenantId = "t2" });
        var result = await _store.GetOrganizationUnitsAsync();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOrganizationUnits_FiltersByTenant()
    {
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-1", Name = "Eng", TenantId = "t1" });
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-2", Name = "HR", TenantId = "t2" });
        var result = await _store.GetOrganizationUnitsAsync("t1");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("dept-1");
    }

    [Fact]
    public async Task SaveMembership_And_GetByUser_Returns_Membership()
    {
        var membership = new UserOrganizationMembership { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true, IsPrimary = true, PositionId = "pos-1" };
        await _store.SaveMembershipAsync(membership);
        var result = await _store.GetMembershipsByUserAsync("user-1");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("m-1");
        result[0].PositionId.Should().Be("pos-1");
        result[0].Should().NotBeSameAs(membership);
    }

    [Fact]
    public async Task GetMembershipsByOrganizationUnit_Returns_CorrectMemberships()
    {
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-1", UserId = "u1", OrganizationUnitId = "dept-1" });
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-2", UserId = "u2", OrganizationUnitId = "dept-1" });
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-3", UserId = "u3", OrganizationUnitId = "dept-2" });
        var result = await _store.GetMembershipsByOrganizationUnitAsync("dept-1");
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAndGetRoleAssignment_Returns_CorrectAssignment()
    {
        var assignment = new UserOrganizationRoleAssignment { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true };
        await _store.SaveRoleAssignmentAsync(assignment);
        var result = await _store.GetRoleAssignmentsByUserAsync("user-1");
        result.Should().HaveCount(1);
        result[0].RoleId.Should().Be("admin");
        result[0].Should().NotBeSameAs(assignment);
    }

    [Fact]
    public async Task GetRoleAssignments_ReturnsEmpty_WhenNoAssignments()
    {
        var result = await _store.GetRoleAssignmentsByUserAsync("user-unknown");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndGetPosition_Works()
    {
        var position = new Position { Id = "pos-1", Name = "Manager", Code = "MGR" };
        await _store.SavePositionAsync(position);
        var result = await _store.GetPositionByIdAsync("pos-1");
        result.Should().NotBeNull();
        result!.Name.Should().Be("Manager");
        result.Should().NotBeSameAs(position);
    }

    [Fact]
    public async Task GetPositions_FiltersByTenant()
    {
        await _store.SavePositionAsync(new Position { Id = "pos-1", Name = "MGR", TenantId = "t1" });
        await _store.SavePositionAsync(new Position { Id = "pos-2", Name = "DEV", TenantId = "t2" });
        var result = await _store.GetPositionsAsync("t1");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("pos-1");
    }
}
